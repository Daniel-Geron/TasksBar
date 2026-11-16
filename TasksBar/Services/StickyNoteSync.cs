using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;
using TasksBar.Services.Sync;

namespace TasksBar.Services
{
    /// <summary>
    /// Mirrors sticky notes into a dedicated task list on the active sync service (Google Tasks or
    /// CalDAV/Tasks.org) so they survive across devices and can be read in other task apps. One task
    /// per note: the title is the note's first line, the details hold the note text, then (only when
    /// not default) a small settings block, then the drawing data. Content that doesn't fit in one
    /// task's details continues in numbered subtasks of that task.
    /// Everything runs on the UI thread; a semaphore keeps pushes and pulls from overlapping.
    /// </summary>
    public static class StickyNoteSync
    {
        public const string NotesListTitle = "TasksBar Sticky Notes";

        // Google rejects notes longer than 8192 characters; keep a little headroom (used for every service)
        private const int MaxNotesLength = 8000;
        private const string DrawingMarker = "──── TasksBar drawing data (don't edit below) ────";
        private const string SettingsMarker = "──── TasksBar note settings (don't edit below) ────";
        private const string OpenOnStartupOff = "open-on-startup: off";
        private const string TitlePrefix = "title: ";
        private const string PartTitlePrefix = "TasksBar note data, part ";

        // Pasted images: the note's details list where each image sits ("id x y width height"), and the
        // JPEG bytes go into subtasks titled "TasksBar image data {id}, part i of n". Those are uploaded
        // once per image, so editing the note's text never re-sends them.
        private const string ImagesMarker = "──── TasksBar note images (don't edit below) ────";
        private const string ImagePartPrefix = "TasksBar image data ";

        private static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan PushDelay = TimeSpan.FromSeconds(2);
        private static System.Windows.Threading.DispatcherTimer? _pushTimer;
        private static System.Windows.Threading.DispatcherTimer? _pullTimer;
        private static readonly TimeSpan PullInterval = TimeSpan.FromMinutes(5);

        public static bool IsEnabled => TaskSync.IsEnabled && AppConfig.Settings.SyncStickyNotes;

        /// <summary>True for the hidden notes list, so list pickers can skip it.</summary>
        public static bool IsNotesList(string? id, string? title) =>
            (!string.IsNullOrEmpty(id) && id == AppConfig.Settings.StickyNotesListId) || title == NotesListTitle;

        // ------------------------------------------------------------------ local change tracking

        /// <summary>Call after a local edit. Saves to disk and schedules an upload.</summary>
        public static void MarkDirty(StickyNoteModel note)
        {
            note.IsDirty = true;
            LocalDataManager.SaveNotes();
            SchedulePush();
        }

        private static void SchedulePush()
        {
            if (!IsEnabled) return;

            if (_pushTimer == null)
            {
                _pushTimer = new System.Windows.Threading.DispatcherTimer { Interval = PushDelay };
                _pushTimer.Tick += async (s, e) =>
                {
                    _pushTimer.Stop();
                    await PushDirtyAsync();
                };
            }

            // Restart the countdown so typing doesn't upload on every keystroke
            _pushTimer.Stop();
            _pushTimer.Start();
        }

        // Picks up edits from other devices while the app keeps running
        private static void EnsurePeriodicPull()
        {
            if (_pullTimer != null) return;
            _pullTimer = new System.Windows.Threading.DispatcherTimer { Interval = PullInterval };
            _pullTimer.Tick += async (s, e) => await SyncAsync();
            _pullTimer.Start();
        }

        // ------------------------------------------------------------------ public entry points

        /// <summary>Applies changes made on other devices, then uploads local changes.</summary>
        public static async Task SyncAsync()
        {
            EnsurePeriodicPull();
            if (!IsEnabled) return;
            var backend = TaskSync.Current;
            if (backend == null) return;

            await _gate.WaitAsync();
            try
            {
                EnsureSameAccount();
                string listId = await EnsureNotesListAsync(backend);
                await FlushPendingDeletesAsync(backend, listId);
                // Pull first, so notes this device hasn't linked yet (first sync with an account)
                // are matched to their copy on the server instead of being uploaded twice
                await PullCoreAsync(backend, listId);
                await PushDirtyCoreAsync(backend, listId);
            }
            catch (Exception ex)
            {
                // Offline or API hiccup: dirty flags stay set, so the next sync retries
                System.Diagnostics.Debug.WriteLine($"Sticky note sync failed: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        public static async Task PushDirtyAsync()
        {
            if (!IsEnabled) return;
            var backend = TaskSync.Current;
            if (backend == null) return;

            // Not linked with this account yet: a full sync matches notes against the server first
            if (AppConfig.Settings.NotesSyncAccount != TaskSync.AccountKey || string.IsNullOrEmpty(AppConfig.Settings.StickyNotesListId))
            {
                await SyncAsync();
                return;
            }

            await _gate.WaitAsync();
            try
            {
                string listId = await EnsureNotesListAsync(backend);
                await PushDirtyCoreAsync(backend, listId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sticky note upload failed: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Removes the note's remote task. Queued and retried if it can't be done now.</summary>
        public static async Task DeleteNoteAsync(StickyNoteModel note)
        {
            if (string.IsNullOrEmpty(note.GoogleTaskId)) return;

            AppConfig.Settings.PendingNoteDeletes.Add(note.GoogleTaskId);
            AppConfig.Settings.PendingNoteDeletes.AddRange(note.PartTaskIds);
            AppConfig.Settings.PendingNoteDeletes.AddRange(note.ImageTaskIds.Values.SelectMany(ids => ids));
            AppConfig.Save();

            if (!IsEnabled) return;
            var backend = TaskSync.Current;
            if (backend == null) return;

            await _gate.WaitAsync();
            try
            {
                EnsureSameAccount();
                string listId = await EnsureNotesListAsync(backend);
                await FlushPendingDeletesAsync(backend, listId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sticky note delete failed: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        // ------------------------------------------------------------------ account switching

        /// <summary>
        /// The notes' remote ids only mean something on the account they were synced with. After
        /// switching (e.g. Google to Tasks.org) they're cleared, and the next pull links each note to
        /// an identical one already on the new account, or uploads it.
        /// </summary>
        private static void EnsureSameAccount()
        {
            string account = TaskSync.AccountKey;
            string previous = AppConfig.Settings.NotesSyncAccount;
            if (previous == account) return;

            // Before this setting existed, notes could only have been synced with Google
            bool upgradeFromGoogleOnly = previous.Length == 0 && account == "google";
            if (!upgradeFromGoogleOnly)
            {
                foreach (var note in LocalDataManager.ActiveNotes)
                {
                    note.GoogleTaskId = null;
                    note.PartTaskIds.Clear();
                    note.ImageTaskIds.Clear();
                    note.RemoteUpdated = null;
                    note.IsDirty = true;
                }
                AppConfig.Settings.StickyNotesListId = "";
                AppConfig.Settings.PendingNoteDeletes.Clear();
                LocalDataManager.SaveNotes();
            }

            AppConfig.Settings.NotesSyncAccount = account;
            AppConfig.Save();
        }

        // ------------------------------------------------------------------ list management

        private static async Task<string> EnsureNotesListAsync(ITaskBackend backend)
        {
            var lists = await backend.GetListsAsync();

            // Prefer the remembered id (survives a rename in another app), then the title
            var existing = lists.FirstOrDefault(l => l.Id == AppConfig.Settings.StickyNotesListId)
                        ?? lists.FirstOrDefault(l => l.Title == NotesListTitle);

            existing ??= await backend.CreateListAsync(NotesListTitle);

            if (AppConfig.Settings.StickyNotesListId != existing.Id)
            {
                AppConfig.Settings.StickyNotesListId = existing.Id;
                AppConfig.Save();
            }

            return existing.Id;
        }

        private static async Task FlushPendingDeletesAsync(ITaskBackend backend, string listId)
        {
            var pending = AppConfig.Settings.PendingNoteDeletes;
            if (pending.Count == 0) return;

            foreach (var id in pending.ToList())
            {
                try
                {
                    await backend.DeleteTaskAsync(listId, id);
                    pending.Remove(id);
                }
                catch (RemoteNotFoundException)
                {
                    pending.Remove(id); // Already gone
                }
            }
            AppConfig.Save();
        }

        private static async Task DeleteQuietlyAsync(ITaskBackend backend, string listId, string taskId)
        {
            try { await backend.DeleteTaskAsync(listId, taskId); }
            catch (RemoteNotFoundException) { }
        }

        // ------------------------------------------------------------------ upload

        private static async Task PushDirtyCoreAsync(ITaskBackend backend, string listId)
        {
            var toPush = LocalDataManager.ActiveNotes
                .Where(n => n.IsDirty || string.IsNullOrEmpty(n.GoogleTaskId))
                .ToList();

            foreach (var note in toPush)
            {
                await PushNoteAsync(backend, listId, note);
            }

            if (toPush.Count > 0) LocalDataManager.SaveNotes();
        }

        private static async Task PushNoteAsync(ITaskBackend backend, string listId, StickyNoteModel note)
        {
            // Snapshot what we send, so an edit made while the request is in flight stays dirty
            string text = note.Text ?? "";
            string ink = note.InkData ?? "";
            bool openOnStartup = note.OpenOnStartup;
            string title = CleanTitle(note.Title);
            string images = FormatImages(note.Images);

            List<string> chunks = SplitPayload(BuildPayload(text, ink, openOnStartup, title, images));
            var body = new RemoteTask
            {
                Title = title.Length > 0 ? title : MakeTitle(text, ink, note.Images.Count > 0),
                Notes = chunks[0]
            };

            RemoteTask saved;
            if (string.IsNullOrEmpty(note.GoogleTaskId))
            {
                note.ImageTaskIds.Clear();
                saved = await backend.CreateTaskAsync(listId, body);
            }
            else
            {
                try
                {
                    body.Id = note.GoogleTaskId;
                    saved = await backend.UpdateTaskAsync(listId, body);
                }
                catch (RemoteNotFoundException)
                {
                    // Deleted remotely while edited here: the local edit wins and recreates it
                    body.Id = "";
                    note.PartTaskIds.Clear();
                    note.ImageTaskIds.Clear(); // Went away with the deleted task
                    saved = await backend.CreateTaskAsync(listId, body);
                }
            }

            note.GoogleTaskId = saved.Id;

            // Replace overflow subtasks (rare: only very long notes or big drawings)
            foreach (var oldPart in note.PartTaskIds)
            {
                await DeleteQuietlyAsync(backend, listId, oldPart);
            }
            note.PartTaskIds.Clear();

            for (int i = 1; i < chunks.Count; i++)
            {
                var part = await backend.CreateTaskAsync(listId, new RemoteTask
                {
                    Title = $"{PartTitlePrefix}{i + 1} of {chunks.Count}",
                    Notes = chunks[i],
                    Parent = saved.Id
                });
                note.PartTaskIds.Add(part.Id);
            }

            await PushImagesAsync(backend, listId, note, saved.Id);

            note.RemoteUpdated = saved.Updated;
            if (note.Text == text && note.InkData == ink && note.OpenOnStartup == openOnStartup &&
                CleanTitle(note.Title) == title && FormatImages(note.Images) == images) note.IsDirty = false;
        }

        // Uploads images this note hasn't uploaded yet and deletes the data of images it no longer has
        private static async Task PushImagesAsync(ITaskBackend backend, string listId, StickyNoteModel note, string noteTaskId)
        {
            var wanted = note.Images.Select(i => i.Id).Distinct().ToList();

            foreach (var id in note.ImageTaskIds.Keys.Except(wanted).ToList())
            {
                foreach (var taskId in note.ImageTaskIds[id])
                {
                    await DeleteQuietlyAsync(backend, listId, taskId);
                }
                note.ImageTaskIds.Remove(id);
            }

            foreach (var id in wanted)
            {
                // Already uploaded, or not downloaded to this device yet (so nothing to upload)
                if (note.ImageTaskIds.ContainsKey(id) || !NoteImageStore.Exists(id)) continue;

                var chunks = SplitPayload(Convert.ToBase64String(NoteImageStore.ReadBytes(id)));
                var partIds = new List<string>();
                for (int i = 0; i < chunks.Count; i++)
                {
                    var part = await backend.CreateTaskAsync(listId, new RemoteTask
                    {
                        Title = $"{ImagePartPrefix}{id}, part {i + 1} of {chunks.Count}",
                        Notes = chunks[i],
                        Parent = noteTaskId
                    });
                    partIds.Add(part.Id);
                }

                note.ImageTaskIds[id] = partIds;
                LocalDataManager.SaveNotes(); // A long upload that gets interrupted keeps the images already done
            }
        }

        // ------------------------------------------------------------------ download

        private static bool IsDataPart(RemoteTask task) =>
            task.Title.StartsWith(PartTitlePrefix, StringComparison.Ordinal) ||
            task.Title.StartsWith(ImagePartPrefix, StringComparison.Ordinal);

        // "TasksBar note data, part 3 of 4" -> 3
        private static int PartNumber(string title)
        {
            var match = Regex.Match(title, @"part (\d+) of \d+$");
            return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
        }

        private static async Task PullCoreAsync(ITaskBackend backend, string listId)
        {
            // List without the details where the service allows it: image subtasks are large, so details are
            // only downloaded for notes that changed and for images this device doesn't have yet
            var all = await backend.GetTasksAsync(listId, includeCompleted: false, includeNotes: false);
            var ids = new HashSet<string>(all.Select(t => t.Id));

            var parents = all.Where(t => string.IsNullOrEmpty(t.Parent) && !IsDataPart(t)).ToList();
            var childrenByParent = all.Where(t => !string.IsNullOrEmpty(t.Parent))
                                      .GroupBy(t => t.Parent!)
                                      .ToDictionary(g => g.Key, g => g.ToList());

            // Data subtasks whose note is gone (CalDAV doesn't delete subtasks along with their parent)
            foreach (var orphan in all.Where(t => IsDataPart(t) && (string.IsNullOrEmpty(t.Parent) || !ids.Contains(t.Parent!))).ToList())
            {
                await DeleteQuietlyAsync(backend, listId, orphan.Id);
            }

            bool changed = false;
            int newCount = 0;

            foreach (var listed in parents)
            {
                var local = LocalDataManager.ActiveNotes.FirstOrDefault(n => n.GoogleTaskId == listed.Id);
                childrenByParent.TryGetValue(listed.Id, out var children);
                children ??= new List<RemoteTask>();

                // Skip our own last upload, or a local edit that will be uploaded and win
                bool contentChanged = false;
                bool isNew = local == null;
                if (local == null || (local.RemoteUpdated != listed.Updated && !local.IsDirty))
                {
                    var task = await backend.GetTaskAsync(listId, listed.Id);
                    var orderedParts = children
                        .Where(p => p.Title.StartsWith(PartTitlePrefix, StringComparison.Ordinal))
                        .OrderBy(p => PartNumber(p.Title))
                        .ToList();

                    // Parts only continue a full first chunk. If someone shortened the details in
                    // another app, the old parts are stale and would append junk to the text.
                    bool partsAreCurrent = task.Notes.Length >= MaxNotesLength - 1;

                    var payload = new StringBuilder(task.Notes);
                    if (partsAreCurrent)
                    {
                        foreach (var part in orderedParts)
                            payload.Append((await backend.GetTaskAsync(listId, part.Id)).Notes);
                    }
                    ParsePayload(payload.ToString(), out string text, out string ink, out bool openOnStartup, out string title, out var images);

                    if (local == null)
                    {
                        // The same note may already be here but not linked yet (first sync with this account)
                        local = LocalDataManager.ActiveNotes.FirstOrDefault(n =>
                            string.IsNullOrEmpty(n.GoogleTaskId) && (n.Text ?? "") == text && (n.InkData ?? "") == ink);
                        if (local != null)
                        {
                            isNew = false;
                            local.ImageTaskIds.Clear();
                        }
                    }

                    if (local == null)
                    {
                        // Created on another device (or in another task app). Positions are per-device.
                        local = new StickyNoteModel();
                        LocalDataManager.ActiveNotes.Add(local);
                    }

                    local.GoogleTaskId = task.Id;
                    local.Text = text;
                    local.InkData = ink;
                    local.OpenOnStartup = openOnStartup;
                    local.Title = title;
                    local.Images = images;
                    // Data of images removed on the other device was deleted there along with them
                    foreach (var id in local.ImageTaskIds.Keys.Except(images.Select(i => i.Id)).ToList())
                        local.ImageTaskIds.Remove(id);
                    local.PartTaskIds = orderedParts.Select(p => p.Id).ToList(); // stale ones get deleted on the next upload
                    local.RemoteUpdated = task.Updated;
                    local.IsDirty = false;
                    changed = true;
                    contentChanged = true;
                }

                var (imagesArrived, imageRecordsChanged) = await DownloadImagesAsync(backend, listId, local, children);
                if (imagesArrived || imageRecordsChanged) changed = true;
                if (!contentChanged && !imagesArrived) continue;

                var window = FindWindow(local);
                if (window != null)
                {
                    window.ReloadFromModel();
                }
                else if (isNew && local.OpenOnStartup)
                {
                    // A note arriving from another device opens like it would at startup.
                    // Closed notes that were only edited elsewhere stay closed.
                    window = new StickyNoteWindow(local);
                    window.Show();

                    // Cascade new notes in from the top-right corner, sized by the real window
                    var area = SystemParameters.WorkArea;
                    window.Left = Math.Max(area.Left, area.Right - window.ActualWidth - 40 - 30 * newCount);
                    window.Top = area.Top + 40 + 30 * newCount;
                    local.Left = window.Left;
                    local.Top = window.Top;
                    newCount++;
                }
            }

            // Synced notes whose task is gone were deleted or completed elsewhere
            var remoteIds = new HashSet<string>(parents.Select(t => t.Id));
            var removed = LocalDataManager.ActiveNotes
                .Where(n => !string.IsNullOrEmpty(n.GoogleTaskId) && !n.IsDirty && !remoteIds.Contains(n.GoogleTaskId))
                .ToList();

            foreach (var note in removed)
            {
                LocalDataManager.ActiveNotes.Remove(note);
                FindWindow(note)?.Close();
                changed = true;
            }

            if (changed) LocalDataManager.SaveNotes();
        }

        /// <summary>
        /// Downloads the note's images this device doesn't have yet, once all their parts have arrived.
        /// Also remembers which subtasks hold each image so this device never uploads it a second time.
        /// </summary>
        private static async Task<(bool Downloaded, bool RecordsChanged)> DownloadImagesAsync(
            ITaskBackend backend, string listId, StickyNoteModel note, List<RemoteTask> children)
        {
            bool downloaded = false, recordsChanged = false;

            foreach (var id in note.Images.Select(i => i.Id).Distinct())
            {
                string prefix = $"{ImagePartPrefix}{id}, part ";
                var parts = new Dictionary<int, RemoteTask>();
                int total = 0;
                foreach (var child in children.Where(c => c.Title.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    var match = Regex.Match(child.Title.Substring(prefix.Length), @"^(\d+) of (\d+)$");
                    if (!match.Success) continue;
                    parts[int.Parse(match.Groups[1].Value)] = child;
                    total = int.Parse(match.Groups[2].Value);
                }

                // Still uploading on the other device: try again on the next sync
                if (total == 0 || !Enumerable.Range(1, total).All(parts.ContainsKey)) continue;

                if (!note.ImageTaskIds.ContainsKey(id))
                {
                    note.ImageTaskIds[id] = Enumerable.Range(1, total).Select(i => parts[i].Id).ToList();
                    recordsChanged = true;
                }

                if (NoteImageStore.Exists(id)) continue;

                var base64 = new StringBuilder();
                for (int i = 1; i <= total; i++)
                    base64.Append((await backend.GetTaskAsync(listId, parts[i].Id)).Notes);

                try
                {
                    byte[] bytes = Convert.FromBase64String(new string(base64.ToString().Where(c => !char.IsWhiteSpace(c)).ToArray()));
                    // The id is the hash of the bytes, so damaged data is never shown
                    if (NoteImageStore.HashOf(bytes) == id)
                    {
                        NoteImageStore.SaveBytes(bytes);
                        downloaded = true;
                    }
                }
                catch (FormatException) { }
            }

            return (downloaded, recordsChanged);
        }

        private static StickyNoteWindow? FindWindow(StickyNoteModel note) =>
            Application.Current.Windows.OfType<StickyNoteWindow>().FirstOrDefault(w => w.GetModel() == note);

        // ------------------------------------------------------------------ payload format

        public static bool HasStrokes(string? ink)
        {
            if (string.IsNullOrEmpty(ink)) return false;
            try
            {
                using var ms = new MemoryStream(Convert.FromBase64String(ink));
                return new StrokeCollection(ms).Count > 0;
            }
            catch { return false; }
        }

        private static string BuildPayload(string text, string ink, bool openOnStartup, string title, string images)
        {
            string payload = text;
            // Only written when not default, so notes on the default settings look exactly as before
            var settings = new List<string>();
            if (!openOnStartup) settings.Add(OpenOnStartupOff);
            if (title.Length > 0) settings.Add(TitlePrefix + title);
            if (settings.Count > 0) payload += $"\n\n{SettingsMarker}\n{string.Join("\n", settings)}";
            if (images.Length > 0) payload += $"\n\n{ImagesMarker}\n{images}";
            if (HasStrokes(ink)) payload += $"\n\n{DrawingMarker}\n{ink}";
            return payload;
        }

        // One line per image: "id x y width height [rotation]" (rotation only when the image is turned)
        private static string FormatImages(List<NoteImage> images) =>
            string.Join("\n", images.Select(i =>
            {
                var values = new List<double> { i.X, i.Y, i.Width, i.Height };
                if (i.Rotation != 0) values.Add(i.Rotation);
                return i.Id + " " + string.Join(" ", values.Select(v => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));
            }));

        private static List<NoteImage> ParseImages(string block)
        {
            var images = new List<NoteImage>();
            foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 5 && parts.Length != 6) continue;
                var numbers = new double[5];
                bool valid = true;
                for (int i = 1; i < parts.Length; i++)
                    valid &= double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out numbers[i - 1]);
                if (!valid || numbers[2] <= 0 || numbers[3] <= 0) continue;
                images.Add(new NoteImage { Id = parts[0], X = numbers[0], Y = numbers[1], Width = numbers[2], Height = numbers[3], Rotation = numbers[4] });
            }
            return images;
        }

        private static void ParsePayload(string payload, out string text, out string ink, out bool openOnStartup, out string title, out List<NoteImage> images)
        {
            // Each block runs until the next block's marker or the end, so their order doesn't matter
            int drawing = payload.LastIndexOf(DrawingMarker, StringComparison.Ordinal);
            int settings = payload.LastIndexOf(SettingsMarker, StringComparison.Ordinal);
            int imageList = payload.LastIndexOf(ImagesMarker, StringComparison.Ordinal);
            var starts = new[] { drawing, settings, imageList };

            string BlockAt(int start, string marker)
            {
                int end = starts.Where(s => s > start).DefaultIfEmpty(payload.Length).Min();
                return payload.Substring(start + marker.Length, end - start - marker.Length);
            }

            ink = "";
            if (drawing >= 0)
            {
                // Base64 has no whitespace; strip any line breaks an editor may have added
                ink = new string(BlockAt(drawing, DrawingMarker).Where(c => !char.IsWhiteSpace(c)).ToArray());
                if (!HasStrokes(ink)) ink = "";
            }

            openOnStartup = true;
            title = "";
            if (settings >= 0)
            {
                string block = BlockAt(settings, SettingsMarker);
                openOnStartup = !block.Contains(OpenOnStartupOff, StringComparison.OrdinalIgnoreCase);
                title = block.Split('\n')
                             .Select(l => l.Trim())
                             .Where(l => l.StartsWith(TitlePrefix, StringComparison.OrdinalIgnoreCase))
                             .Select(l => l.Substring(TitlePrefix.Length).Trim())
                             .FirstOrDefault() ?? "";
            }

            images = imageList >= 0 ? ParseImages(BlockAt(imageList, ImagesMarker)) : new List<NoteImage>();

            int textEnd = starts.Where(i => i >= 0).DefaultIfEmpty(payload.Length).Min();
            text = payload.Substring(0, textEnd);

            // Remove only the blank line BuildPayload puts before a block, so the note's own trailing newlines survive
            if (textEnd < payload.Length)
            {
                if (text.EndsWith("\n\n", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 2);
                else text = text.TrimEnd('\n', '\r');
            }
        }

        private static List<string> SplitPayload(string payload)
        {
            var chunks = new List<string>();
            for (int i = 0; i < payload.Length; i += MaxNotesLength)
            {
                int len = Math.Min(MaxNotesLength, payload.Length - i);
                // Don't split a surrogate pair (emoji) across two tasks
                if (len == MaxNotesLength && char.IsHighSurrogate(payload[i + len - 1])) len--;
                chunks.Add(payload.Substring(i, len));
                i -= MaxNotesLength - len;
            }
            if (chunks.Count == 0) chunks.Add("");
            return chunks;
        }

        /// <summary>A title is one line (it's stored as a line in the note's settings block).</summary>
        public static string CleanTitle(string? title) =>
            string.Join(" ", (title ?? "").Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

        private static string MakeTitle(string text, string ink, bool hasImages)
        {
            string firstLine = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            if (firstLine.Length > 100) firstLine = firstLine.Substring(0, 100) + "…";
            if (firstLine.Length > 0) return firstLine;
            if (hasImages) return "Sticky note (image)";
            return HasStrokes(ink) ? "Sticky note (drawing)" : "Sticky note";
        }
    }
}
