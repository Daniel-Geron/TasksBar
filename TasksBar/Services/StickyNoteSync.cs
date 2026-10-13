using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// line, the details hold the note text followed by the drawing data. Content that doesn't fit
    /// in Google's details limit continues in numbered subtasks of that task.
    /// Everything runs on the UI thread; a semaphore keeps pushes and pulls from overlapping.
    /// </summary>
    public static class StickyNoteSync
    {
        public const string NotesListTitle = "TasksBar Sticky Notes";

        // Google rejects notes longer than 8192 characters; keep a little headroom
        private const int MaxNotesLength = 8000;
        private const string DrawingMarker = "──── TasksBar drawing data (don't edit below) ────";
        private const string PartTitlePrefix = "TasksBar note data, part ";

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

            List<string> chunks = SplitPayload(BuildPayload(text, ink));
            var body = new GTask
            {
                Title = MakeTitle(text, ink),
                Notes = chunks[0],
                Status = "needsAction"
            };

            GTask saved;
            if (string.IsNullOrEmpty(note.GoogleTaskId))
            {
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

            note.RemoteUpdated = saved.Updated;
            if (note.Text == text && note.InkData == ink) note.IsDirty = false;
        }

        // ------------------------------------------------------------------ download

        private static async Task PullCoreAsync(TasksService service, string listId)
        {
            var all = new List<GTask>();
            var request = service.Tasks.List(listId);
            request.ShowCompleted = false;
            request.ShowHidden = false;
            request.ShowDeleted = false;
            request.MaxResults = 100;
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

            foreach (var task in parents)
            {
                var local = LocalDataManager.ActiveNotes.FirstOrDefault(n => n.GoogleTaskId == task.Id);

                // Our own last upload, or a local edit that will be uploaded and win
                if (local != null && (local.RemoteUpdated == task.Updated || local.IsDirty)) continue;

                childrenByParent.TryGetValue(task.Id, out var parts);
                parts ??= new List<GTask>();
                var orderedParts = parts
                    .Where(p => (p.Title ?? "").StartsWith(PartTitlePrefix, StringComparison.Ordinal))
                    .OrderBy(p => p.Position, StringComparer.Ordinal)
                    .ToList();

                // Parts only continue a full first chunk. If someone shortened the details in the
                // Google Tasks app, the old parts are stale and would append junk to the text.
                bool partsAreCurrent = (task.Notes ?? "").Length >= MaxNotesLength - 1;

                string payload = (task.Notes ?? "") +
                    (partsAreCurrent ? string.Concat(orderedParts.Select(p => p.Notes ?? "")) : "");
                ParsePayload(payload, out string text, out string ink);

                bool isNew = local == null;
                if (local == null)
                {
                    // Created on another device (or in the Google Tasks app). Positions are per-device.
                    local = new StickyNoteModel();
                    LocalDataManager.ActiveNotes.Add(local);
                }

                local.GoogleTaskId = task.Id;
                local.Text = text;
                local.InkData = ink;
                local.PartTaskIds = orderedParts.Select(p => p.Id).ToList(); // stale ones get deleted on the next upload
                local.RemoteUpdated = task.Updated;
                local.IsDirty = false;
                changed = true;

                var window = FindWindow(local);
                if (window != null)
                {
                    window.ReloadFromModel();
                }
                else
                {
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

        private static string BuildPayload(string text, string ink)
        {
            if (!HasStrokes(ink)) return text;
            return $"{text}\n\n{DrawingMarker}\n{ink}";
        }

        private static void ParsePayload(string payload, out string text, out string ink)
        {
            int marker = payload.LastIndexOf(DrawingMarker, StringComparison.Ordinal);
            if (marker < 0)
            {
                text = payload;
                ink = "";
                return;
            }

            text = payload.Substring(0, marker).TrimEnd('\n', '\r');
            // Base64 has no whitespace; strip any line breaks an editor may have added
            ink = new string(payload.Substring(marker + DrawingMarker.Length).Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (!HasStrokes(ink)) ink = "";
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

        private static string MakeTitle(string text, string ink)
        {
            string firstLine = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            if (firstLine.Length > 100) firstLine = firstLine.Substring(0, 100) + "…";
            if (firstLine.Length > 0) return firstLine;
            return HasStrokes(ink) ? "Sticky note (drawing)" : "Sticky note";
        }
    }
}
