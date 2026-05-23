using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TasksBar
{
    // Remembers the text and exact screen position of a sticky note
    public class StickyNoteModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Text { get; set; } = "";
        public double Left { get; set; } = 0;
        public double Top { get; set; } = 0;
    }

    public static class LocalDataManager
    {
        private static readonly string TasksFile = Path.Combine(AppContext.BaseDirectory, "local_tasks.json");
        private static readonly string NotesFile = Path.Combine(AppContext.BaseDirectory, "local_notes.json");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        // --- LOCAL TASKS ---
        public static void SaveTasks(IEnumerable<TaskItem> tasks)
        {
            File.WriteAllText(TasksFile, JsonSerializer.Serialize(tasks, JsonOptions));
        }

        public static List<TaskItem> LoadTasks()
        {
            if (!File.Exists(TasksFile)) return new List<TaskItem>();
            try { return JsonSerializer.Deserialize<List<TaskItem>>(File.ReadAllText(TasksFile)) ?? new List<TaskItem>(); }
            catch { return new List<TaskItem>(); }
        }

        // --- STICKY NOTES ---
        public static List<StickyNoteModel> ActiveNotes { get; set; } = new List<StickyNoteModel>();

        public static void SaveNotes()
        {
            File.WriteAllText(NotesFile, JsonSerializer.Serialize(ActiveNotes, JsonOptions));
        }

        public static void LoadNotes()
        {
            if (!File.Exists(NotesFile)) return;
            try { ActiveNotes = JsonSerializer.Deserialize<List<StickyNoteModel>>(File.ReadAllText(NotesFile)) ?? new List<StickyNoteModel>(); }
            catch { ActiveNotes = new List<StickyNoteModel>(); }
        }
    }
}