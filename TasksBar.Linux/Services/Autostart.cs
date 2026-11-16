using System;
using System.IO;

namespace TasksBar.Linux.Services
{
    /// <summary>
    /// "Launch on Startup": an entry in ~/.config/autostart, the standard way GNOME, KDE and other
    /// desktops start apps when you log in.
    /// </summary>
    public static class Autostart
    {
        private static string EntryPath => Path.Combine(ConfigHome, "autostart", "tasksbar.desktop");

        public static string ConfigHome =>
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        /// <summary>The command that starts this copy of TasksBar (the installed /usr/bin/tasksbar when there is one).</summary>
        public static string ProgramPath => Environment.ProcessPath ?? "tasksbar";

        /// <summary>Adds or removes the entry. Returns an error message, or null when it worked.</summary>
        public static string? Apply(bool enable)
        {
            try
            {
                if (!enable)
                {
                    if (File.Exists(EntryPath)) File.Delete(EntryPath);
                    return null;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
                File.WriteAllText(EntryPath,
                    "[Desktop Entry]\n" +
                    "Type=Application\n" +
                    "Name=TasksBar\n" +
                    "Comment=Your tasks and sticky notes\n" +
                    $"Exec={DesktopExec(ProgramPath)}\n" +
                    "Icon=tasksbar\n" +
                    "Terminal=false\n" +
                    "X-GNOME-Autostart-enabled=true\n");
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Keeps the entry pointing at this copy of TasksBar if it was moved or updated.</summary>
        public static void Refresh()
        {
            if (AppConfig.Settings.LaunchOnStartup) Apply(true);
        }

        /// <summary>Quotes a program path (plus an optional argument) for a .desktop file's Exec line.</summary>
        public static string DesktopExec(string path, string? argument = null)
        {
            string quoted = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\"";
            return argument == null ? quoted : quoted + " " + argument;
        }
    }
}
