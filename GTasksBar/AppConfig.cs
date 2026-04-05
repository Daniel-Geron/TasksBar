using System.IO;
using System.Text.Json;

namespace GTasksBar
{
    // This holds the actual data
    public class AppSettings
    {
        public bool UseAcrylic { get; set; } = true;
        public bool StayOnTop { get; set; } = false;
        public bool IsLocked { get; set; } = true;
        public bool EnableGoogleSync { get; set; } = true;
        public WidgetPosition Position { get; set; } = WidgetPosition.BottomRight;

        // --- NEW ADDITIONS ---
        public bool ShowCompletedTasks { get; set; } = false;
        public bool LaunchOnStartup { get; set; } = false;

        // 0 = System Default, 1 = Light, 2 = Dark
        public int AppTheme { get; set; } = 0;
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