using System;
using System.IO;

namespace TasksBar
{
    public enum WidgetPosition { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }

    /// <summary>
    /// Where TasksBar keeps its files (config.json, token.json, local_*.json, note_images/).
    /// On Windows that's the app's own folder, as it always was. The Linux app points it at
    /// ~/.config/TasksBar before anything is loaded, since its install folder may be read-only.
    /// </summary>
    public static class AppPaths
    {
        public static string DataDirectory { get; set; } = AppContext.BaseDirectory;

        public static string File(string name) => Path.Combine(DataDirectory, name);

        /// <summary>The Google API credentials: in the data folder, or shipped next to the app.</summary>
        public static string CredentialsFile
        {
            get
            {
                string inData = File("credentials.json");
                if (System.IO.File.Exists(inData)) return inData;
                string beside = Path.Combine(AppContext.BaseDirectory, "credentials.json");
                return System.IO.File.Exists(beside) ? beside : inData;
            }
        }

        public static bool HasCredentials => System.IO.File.Exists(CredentialsFile);

        /// <summary>On Linux/macOS, limits a file to the current user (chmod 600). No-op on Windows.</summary>
        public static void MakePrivate(string path)
        {
            if (OperatingSystem.IsWindows() || !System.IO.File.Exists(path)) return;
            try { System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { }
        }
    }
}
