using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;

namespace TasksBar.Linux
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Settings, tasks, notes and sign-ins live in ~/.config/TasksBar (or $XDG_CONFIG_HOME/TasksBar),
            // since the install folder may be read-only. TASKSBAR_DATA_DIR overrides it (used for testing).
            AppPaths.DataDirectory = Environment.GetEnvironmentVariable("TASKSBAR_DATA_DIR") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                      ? xdg
                      : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
                  "TasksBar");
            Directory.CreateDirectory(AppPaths.DataDirectory);

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                // Session management (libICE/libSM) isn't needed by a tray app and is missing on minimal systems
                .With(new X11PlatformOptions { EnableMultiTouch = true, EnableSessionManagement = false })
                .LogToTrace();
    }
}
