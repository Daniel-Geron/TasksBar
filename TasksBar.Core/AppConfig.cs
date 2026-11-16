using System.IO;

using System.Text.Json;


namespace TasksBar

{

    // This holds the actual data

    public class AppSettings

    {

        public bool UseAcrylic { get; set; } = true;

        public bool StayOnTop { get; set; } = false;

        public bool IsLocked { get; set; } = true;

        // Kept for older versions of TasksBar reading this file; SyncProvider is what's used now
        public bool EnableGoogleSync { get; set; } = false;

        // Where tasks sync: "Local" (this PC only), "Google" or "CalDav". See Services/Sync/TaskSync.cs
        public string SyncProvider { get; set; } = "";

        // CalDAV account (Tasks.org Cloud by default). The app password is DPAPI-encrypted for this Windows user.
        public bool CalDavIsTasksOrg { get; set; } = true;
        public string CalDavServerUrl { get; set; } = "https://caldav.tasks.org/";
        public string CalDavUsername { get; set; } = "";
        public string CalDavPassword { get; set; } = "";
        public string CalDavHomeUrl { get; set; } = "";

        // The account the sticky notes' remote ids belong to (TaskSync.AccountKey)
        public string NotesSyncAccount { get; set; } = "";

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

        // System-wide sticky note shortcuts, e.g. "Ctrl+Alt+N" (empty = off). See Services/HotkeyManager.cs
        public string NewNoteHotkey { get; set; } = "";
        public string OpenAllNotesHotkey { get; set; } = "";

    }


    // This manager saves and loads the data

    public static class AppConfig

    {

        private static string SettingsFile => AppPaths.File("config.json");

        public static AppSettings Settings { get; set; } = new AppSettings();

       
        public static void Load()

        {

            if (File.Exists(SettingsFile))

            {

                try

                {

                    var json = File.ReadAllText(SettingsFile);

                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                    // Settings saved before Tasks.org/CalDAV support only had the Google switch
                    if (string.IsNullOrEmpty(Settings.SyncProvider))
                        Settings.SyncProvider = Settings.EnableGoogleSync ? "Google" : "Local";

                }

                catch { /* If file is corrupted, it will just use defaults */ }

            }

        }


        public static void Save()

        {

            // Added WriteIndented so the JSON file is easy to read in Notepad!

            if (string.IsNullOrEmpty(Settings.SyncProvider)) Settings.SyncProvider = "Local";
            Settings.EnableGoogleSync = Settings.SyncProvider == "Google";

            var options = new JsonSerializerOptions { WriteIndented = true };

            var json = JsonSerializer.Serialize(Settings, options);


            File.WriteAllText(SettingsFile, json);

            // Holds the CalDAV app password on Linux (no DPAPI there), so keep it private to this user
            AppPaths.MakePrivate(SettingsFile);

        }

    }

}