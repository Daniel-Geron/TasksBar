using System.IO;

using System.Text.Json;
using Wpf.Ui.Controls;


namespace TasksBar

{

    // This holds the actual data

    public class AppSettings

    {

        public bool UseAcrylic { get; set; } = true;

        public bool StayOnTop { get; set; } = false;

        public bool IsLocked { get; set; } = true;

        public bool EnableGoogleSync { get; set; } = false;

        public bool ShowCompletedTasks { get; set; } = false;

        public WidgetPosition Position { get; set; } = WidgetPosition.BottomRight;

        public bool LaunchOnStartup { get; set; } = false;


        // 0 = System Default, 1 = Light, 2 = Dark

        public int AppTheme { get; set; } = 0;

        public string SelectedListId { get; set; } = "@default";

        public bool EnableStreamDeck { get; set; } = false;

        // Optional sounds for the task completion circle
        public bool EnableHoverSound { get; set; } = false;
        public string HoverSoundPath { get; set; } = "";
        public bool EnableClickSound { get; set; } = false;
        public string ClickSoundPath { get; set; } = "";

        // Sticky notes mirrored to a hidden Google Tasks list (only while Google sync is on)
        public bool SyncStickyNotes { get; set; } = true;
        public string StickyNotesListId { get; set; } = "";
        public System.Collections.Generic.List<string> PendingNoteDeletes { get; set; } = new System.Collections.Generic.List<string>();

    }


    // This manager saves and loads the data

    public static class AppConfig

    {

        private static readonly string SettingsFile = System.IO.Path.Combine(System.AppContext.BaseDirectory, "config.json");

        public static AppSettings Settings { get; set; } = new AppSettings();

       
        public static void Load()

        {

            if (File.Exists(SettingsFile))

            {

                try

                {

                    var json = File.ReadAllText(SettingsFile);

                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                }

                catch { /* If file is corrupted, it will just use defaults */ }

            }

        }


        public static void Save()

        {

            // Added WriteIndented so the JSON file is easy to read in Notepad!

            var options = new JsonSerializerOptions { WriteIndented = true };

            var json = JsonSerializer.Serialize(Settings, options);


            File.WriteAllText(SettingsFile, json);

        }

    }

}