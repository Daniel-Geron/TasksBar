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
using Google.Apis.Tasks.v1;
using GTask = Google.Apis.Tasks.v1.Data.Task;

namespace TasksBar.Services
{
    /// <summary>
    /// Mirrors sticky notes into a dedicated Google Tasks list so they survive across devices
    /// and can be read in the Google Tasks app. One task per note: the title is the note's first
    /// line, the details hold the note text, then (only when not default) a small settings block,
    /// then the drawing data. Content that doesn't fit
    /// in Google's details limit continues in numbered subtasks of that task.
    /// Everything runs on the UI thread; a semaphore keeps pushes and pulls from overlapping.
    /// </summary>
    public static class StickyNoteSync
    {
        public const string NotesListTitle = "TasksBar Sticky Notes";

        // Google rejects notes longer than 8192 characters; keep a little headroom
        private const int MaxNotesLength = 8000;
        private const string DrawingMarker = "──── TasksBar drawing data (don't edit below) ────";
        private const string SettingsMarker = "──── TasksBar note settings (don't edit below) ────";
        private const string OpenOnStartupOff = "open-on-startup: off";
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

        public static bool IsEnabled =>
            AppConfig.Settings.EnableGoogleSync && AppConfig.Settings.SyncStickyNotes;

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

        /// <summary>Uploads local changes, then applies changes made on other devices.</summary>
        public static async Task SyncAsync()
        {
            EnsurePeriodicPull();
            if (!IsEnabled) return;
            var service = GoogleAuthManager.GetTasksService();
            if (service == null) return;

            await _gate.WaitAsync();
            try
            {
                string listId = await EnsureNotesListAsync(service);
                await FlushPendingDeletesAsync(service, listId);
                await PushDirtyCoreAsync(service, listId);
                await PullCoreAsync(service, listId);
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
            var service = GoogleAuthManager.GetTasksService();
            if (service == null) return;

            await _gate.WaitAsync();
            try
            {
                string listId = await EnsureNotesListAsync(service);
                await PushDirtyCoreAsync(service, listId);
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

        /// <summary>Removes the note's Google task. Queued and retried if it can't be done now.</summary>
        public static async Task DeleteNoteAsync(StickyNoteModel note)
        {
            if (string.IsNullOrEmpty(note.GoogleTaskId)) return;

            AppConfig.Settings.PendingNoteDeletes.Add(note.GoogleTaskId);
            AppConfig.Settings.PendingNoteDeletes.AddRange(note.PartTaskIds);
            AppConfig.Settings.PendingNoteDeletes.AddRange(note.ImageTaskIds.Values.SelectMany(ids => ids));
            AppConfig.Save();

            if (!IsEnabled) return;
            var service = GoogleAuthManager.GetTasksService();
            if (service == null) return;

            await _gate.WaitAsync();
            try
            {
                string listId = await EnsureNotesListAsync(service);
                await FlushPendingDeletesAsync(service, listId);
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

        // ------------------------------------------------------------------ list management

        private static async Task<string> EnsureNotesListAsync(TasksService service)
        {
            var lists = new List<Google.Apis.Tasks.v1.Data.TaskList>();
            var request = service.Tasklists.List();
            request.MaxResults = 100;
            do
            {
                var page = await request.ExecuteAsync();
                if (page.Items != null) lists.AddRange(page.Items);
                request.PageToken = page.NextPageToken;
            } while (!string.IsNullOrEmpty(request.PageToken));

            // Prefer the remembered id (survives a rename in the Google app), then the title
            var existing = lists.FirstOrDefault(l => l.Id == AppConfig.Settings.StickyNotesListId)
                        ?? lists.FirstOrDefault(l => l.Title == NotesListTitle);

            if (existing == null)
            {
                existing = await service.Tasklists.Insert(
                    new Google.Apis.Tasks.v1.Data.TaskList { Title = NotesListTitle }).ExecuteAsync();
            }

            if (AppConfig.Settings.StickyNotesListId != existing.Id)
            {
                AppConfig.Settings.StickyNotesListId = existing.Id;
                AppConfig.Save();
            }

            return existing.Id;
        }

        private static async Task FlushPendingDeletesAsync(TasksService service, string listId)
        {
            var pending = AppConfig.Settings.PendingNoteDeletes;
            if (pending.Count == 0) return;

            foreach (var id in pending.ToList())
            {
                try
                {
                    await service.Tasks.Delete(listId, id).ExecuteAsync();
                    pending.Remove(id);
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    pending.Remove(id); // Already gone
                }
            }
            AppConfig.Save();
        }

        // ------------------------------------------------------------------ upload

        private static async Task PushDirtyCoreAsync(TasksService service, string listId)
        {
            var toPush = LocalDataManager.ActiveNotes
                .Where(n => n.IsDirty || string.IsNullOrEmpty(n.GoogleTaskId))
                .ToList();

            foreach (var note in toPush)
            {
                await PushNoteAsync(service, listId, note);
            }

            if (toPush.Count > 0) LocalDataManager.SaveNotes();
        }

        private static async Task PushNoteAsync(TasksService service, string listId, StickyNoteModel note)
        {
            // Snapshot what we send, so an edit made while the request is in flight stays dirty
            string text = note.Text ?? "";
            string ink = note.InkData ?? "";
            bool openOnStartup = note.OpenOnStartup;
            string images = FormatImages(note.Images);

            List<string> chunks = SplitPayload(BuildPayload(text, ink, openOnStartup, images));
            var body = new GTask
            {
                Title = MakeTitle(text, ink, note.Images.Count > 0),
                Notes = chunks[0],
                Status = "needsAction"
            };

            GTask saved;
            if (string.IsNullOrEmpty(note.GoogleTaskId))
            {
                note.ImageTaskIds.Clear();
                saved = await service.Tasks.Insert(body, listId).ExecuteAsync();
            }
            else
            {
                try
                {
                    body.Id = note.GoogleTaskId;
                    saved = await service.Tasks.Update(body, listId, note.GoogleTaskId).ExecuteAsync();
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Deleted remotely while edited here: the local edit wins and recreates it
                    body.Id = null;
                    note.PartTaskIds.Clear();
                    note.ImageTaskIds.Clear(); // Went away with the deleted task
                    saved = await service.Tasks.Insert(body, listId).ExecuteAsync();
                }
            }

            note.GoogleTaskId = saved.Id;

            // Replace overflow subtasks (rare: only very long notes or big drawings)
            foreach (var oldPart in note.PartTaskIds)
            {
                try { await service.Tasks.Delete(listId, oldPart).ExecuteAsync(); }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) { }
            }
            note.PartTaskIds.Clear();

            string? previous = null;
            for (int i = 1; i < chunks.Count; i++)
            {
                var insert = service.Tasks.Insert(new GTask
                {
                    Title = $"{PartTitlePrefix}{i + 1} of {chunks.Count}",
                    Notes = chunks[i]
                }, listId);
                insert.Parent = saved.Id;
                if (previous != null) insert.Previous = previous;

                var part = await insert.ExecuteAsync();
                note.PartTaskIds.Add(part.Id);
                previous = part.Id;
            }

            await PushImagesAsync(service, listId, note, saved.Id);

            note.RemoteUpdated = saved.Updated;
            if (note.Text == text && note.InkData == ink && note.OpenOnStartup == openOnStartup &&
                FormatImages(note.Images) == images) note.IsDirty = false;
        }

        // Uploads images this note hasn't uploaded yet and deletes the data of images it no longer has
        private static async Task PushImagesAsync(TasksService service, string listId, StickyNoteModel note, string noteTaskId)
        {
            var wanted = note.Images.Select(i => i.Id).Distinct().ToList();

            foreach (var id in note.ImageTaskIds.Keys.Except(wanted).ToList())
            {
                foreach (var taskId in note.ImageTaskIds[id])
                {
                    try { await service.Tasks.Delete(listId, taskId).ExecuteAsync(); }
                    catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) { }
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
                    var insert = service.Tasks.Insert(new GTask
                    {
                        Title = $"{ImagePartPrefix}{id}, part {i + 1} of {chunks.Count}",
                        Notes = chunks[i]
                    }, listId);
                    insert.Parent = noteTaskId;
                    var part = await insert.ExecuteAsync();
                    partIds.Add(part.Id);
                }

                note.ImageTaskIds[id] = partIds;
                LocalDataManager.SaveNotes(); // A long upload that gets interrupted keeps the images already done
            }
        }

        // ------------------------------------------------------------------ download

        private static async Task PullCoreAsync(TasksService service, string listId)
        {
            // List without the details: image subtasks are large, so details are only downloaded
            // for notes that changed and for images this device doesn't have yet
            var all = new List<GTask>();
            var request = service.Tasks.List(listId);
            request.ShowCompleted = false;
            request.ShowHidden = false;
            request.ShowDeleted = false;
            request.MaxResults = 100;
            request.Fields = "nextPageToken,items(id,title,parent,updated,position)";
            do
            {
                var page = await request.ExecuteAsync();
                if (page.Items != null) all.AddRange(page.Items);
                request.PageToken = page.NextPageToken;
            } while (!string.IsNullOrEmpty(request.PageToken));

            var parents = all.Where(t => string.IsNullOrEmpty(t.Parent)).ToList();
            var childrenByParent = all.Where(t => !string.IsNullOrEmpty(t.Parent))
                                      .GroupBy(t => t.Parent)
                                      .ToDictionary(g => g.Key, g => g.ToList());

            bool changed = false;
            int newCount = 0;

            foreach (var listed in parents)
            {
                var local = LocalDataManager.ActiveNotes.FirstOrDefault(n => n.GoogleTaskId == listed.Id);
                childrenByParent.TryGetValue(listed.Id, out var children);
                children ??= new List<GTask>();

                // Skip our own last upload, or a local edit that will be uploaded and win
                bool contentChanged = false;
                bool isNew = local == null;
                if (local == null || (local.RemoteUpdated != listed.Updated && !local.IsDirty))
                {
                    var task = await service.Tasks.Get(listId, listed.Id).ExecuteAsync();
                    var orderedParts = children
                        .Where(p => (p.Title ?? "").StartsWith(PartTitlePrefix, StringComparison.Ordinal))
                        .OrderBy(p => p.Position, StringComparer.Ordinal)
                        .ToList();

                    // Parts only continue a full first chunk. If someone shortened the details in the
                    // Google Tasks app, the old parts are stale and would append junk to the text.
                    bool partsAreCurrent = (task.Notes ?? "").Length >= MaxNotesLength - 1;

                    var payload = new StringBuilder(task.Notes ?? "");
                    if (partsAreCurrent)
                    {
                        foreach (var part in orderedParts)
                            payload.Append((await service.Tasks.Get(listId, part.Id).ExecuteAsync()).Notes ?? "");
                    }
                    ParsePayload(payload.ToString(), out string text, out string ink, out bool openOnStartup, out var images);

                    if (local == null)
                    {
                        // Created on another device (or in the Google Tasks app). Positions are per-device.
                        local = new StickyNoteModel();
                        LocalDataManager.ActiveNotes.Add(local);
                    }

                    local.GoogleTaskId = task.Id;
                    local.Text = text;
                    local.InkData = ink;
                    local.OpenOnStartup = openOnStartup;
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

                var (imagesArrived, imageRecordsChanged) = await DownloadImagesAsync(service, listId, local, children);
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

                    if (isNew)
                    {
                        // Cascade new notes in from the top-right corner, sized by the real window
                        var area = SystemParameters.WorkArea;
                        window.Left = Math.Max(area.Left, area.Right - window.ActualWidth - 40 - 30 * newCount);
                        window.Top = area.Top + 40 + 30 * newCount;
                        local.Left = window.Left;
                        local.Top = window.Top;
                        newCount++;
                    }
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
            TasksService service, string listId, StickyNoteModel note, List<GTask> children)
        {
            bool downloaded = false, recordsChanged = false;

            foreach (var id in note.Images.Select(i => i.Id).Distinct())
            {
                string prefix = $"{ImagePartPrefix}{id}, part ";
                var parts = new Dictionary<int, GTask>();
                int total = 0;
                foreach (var child in children.Where(c => (c.Title ?? "").StartsWith(prefix, StringComparison.Ordinal)))
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
                    base64.Append((await service.Tasks.Get(listId, parts[i].Id).ExecuteAsync()).Notes ?? "");

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

        private static bool HasStrokes(string ink)
        {
            if (string.IsNullOrEmpty(ink)) return false;
            try
            {
                using var ms = new MemoryStream(Convert.FromBase64String(ink));
                return new StrokeCollection(ms).Count > 0;
            }
            catch { return false; }
        }

        private static string BuildPayload(string text, string ink, bool openOnStartup, string images)
        {
            string payload = text;
            // Only written when off, so notes on the default setting look exactly as before
            if (!openOnStartup) payload += $"\n\n{SettingsMarker}\n{OpenOnStartupOff}";
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

        private static void ParsePayload(string payload, out string text, out string ink, out bool openOnStartup, out List<NoteImage> images)
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
            if (settings >= 0)
            {
                openOnStartup = !BlockAt(settings, SettingsMarker).Contains(OpenOnStartupOff, StringComparison.OrdinalIgnoreCase);
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
