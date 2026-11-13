using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TasksBar
{
    public class StickyNoteModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Text { get; set; } = "";
        public double Left { get; set; } = 0;
        public double Top { get; set; } = 0;
        public string InkData { get; set; } = "";

        // Whether this note's window opens when TasksBar starts. Synced to Google with the note.
        public bool OpenOnStartup { get; set; } = true;

        // Pasted images floating on the note. The pixels live in note_images/{Id}.jpg (see Services/NoteImageStore.cs)
        public System.Collections.Generic.List<NoteImage> Images { get; set; } = new System.Collections.Generic.List<NoteImage>();

        // Google subtasks already holding each image's data, so an image is uploaded only once
        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> ImageTaskIds { get; set; } = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();

        // Google Tasks sync state (see Services/StickyNoteSync.cs). Position stays per-device.
        public string? GoogleTaskId { get; set; }
        public System.Collections.Generic.List<string> PartTaskIds { get; set; } = new System.Collections.Generic.List<string>();
        public string? RemoteUpdated { get; set; }
        public bool IsDirty { get; set; }
    }

    public class NoteImage
    {
        public string Id { get; set; } = ""; // Hash of the JPEG bytes, so identical images share one file
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; } // Degrees clockwise around the image's center
    }

    public class LocalTaskList
    {
        public string Id { get; set; } = "@default";
        public string Title { get; set; } = "My Tasks";
    }

    public static class LocalDataManager
    {
        private static readonly string NotesFile = Path.Combine(AppContext.BaseDirectory, "local_notes.json");
        private static readonly string ListsFile = Path.Combine(AppContext.BaseDirectory, "local_lists.json");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        // --- LOCAL LISTS ---
        public static System.Collections.Generic.List<LocalTaskList> GetLocalLists()
        {
            if (!File.Exists(ListsFile)) return new System.Collections.Generic.List<LocalTaskList> { new LocalTaskList() };
            try { return JsonSerializer.Deserialize<System.Collections.Generic.List<LocalTaskList>>(File.ReadAllText(ListsFile)) ?? new System.Collections.Generic.List<LocalTaskList> { new LocalTaskList() }; }
            catch { return new System.Collections.Generic.List<LocalTaskList> { new LocalTaskList() }; }
        }

        public static void SaveLocalLists(System.Collections.Generic.List<LocalTaskList> lists)
        {
            File.WriteAllText(ListsFile, JsonSerializer.Serialize(lists, JsonOptions));
        }

        // --- LOCAL TASKS ---
        private static string GetTasksFile(string listId) => Path.Combine(AppContext.BaseDirectory, $"local_tasks_{listId.Replace(":", "_")}.json");

        public static void SaveTasks(IEnumerable<TaskItem> tasks, string listId)
        {
            File.WriteAllText(GetTasksFile(listId), JsonSerializer.Serialize(tasks, JsonOptions));
        }

        public static System.Collections.Generic.List<TaskItem> LoadTasks(string listId)
        {
            string file = GetTasksFile(listId);
            if (!File.Exists(file)) return new System.Collections.Generic.List<TaskItem>();
            try { return JsonSerializer.Deserialize<System.Collections.Generic.List<TaskItem>>(File.ReadAllText(file)) ?? new System.Collections.Generic.List<TaskItem>(); }
            catch { return new System.Collections.Generic.List<TaskItem>(); }
        }

        // --- STICKY NOTES ---
        public static System.Collections.Generic.List<StickyNoteModel> ActiveNotes { get; set; } = new System.Collections.Generic.List<StickyNoteModel>();

        public static void SaveNotes()
        {
            File.WriteAllText(NotesFile, JsonSerializer.Serialize(ActiveNotes, JsonOptions));
        }

        public static void LoadNotes()
        {
            if (!File.Exists(NotesFile)) return;
            try { ActiveNotes = JsonSerializer.Deserialize<System.Collections.Generic.List<StickyNoteModel>>(File.ReadAllText(NotesFile)) ?? new System.Collections.Generic.List<StickyNoteModel>(); }
            catch { ActiveNotes = new System.Collections.Generic.List<StickyNoteModel>(); }
        }
    }
}