using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TasksBar.Linux.Services
{
    /// <summary>
    /// The optional hover and click sounds for the task check circle (same settings as the Windows app).
    /// Played with the desktop's own sound tools: PipeWire's pw-play or PulseAudio's paplay (both read
    /// MP3, WAV, OGG and FLAC), falling back to ffplay or mpg123 when those are missing.
    /// </summary>
    public static class TaskSounds
    {
        private static readonly (string Tool, string[] Args)[] Players =
        {
            ("pw-play", Array.Empty<string>()),
            ("paplay", Array.Empty<string>()),
            ("ffplay", new[] { "-nodisp", "-autoexit", "-loglevel", "quiet" }),
            ("mpg123", new[] { "-q" })
        };

        private static (string Path, string[] Args)? _player;
        private static bool _searched;
        private static Process? _hover, _click;

        public static void PlayHover()
        {
            if (AppConfig.Settings.EnableHoverSound) Play(ref _hover, AppConfig.Settings.HoverSoundPath);
        }

        public static void PlayClick()
        {
            if (AppConfig.Settings.EnableClickSound) Play(ref _click, AppConfig.Settings.ClickSoundPath);
        }

        /// <summary>Plays a file once, ignoring the on/off switches (the preview button in Settings).</summary>
        public static void Preview(string? path)
        {
            Process? process = null;
            Play(ref process, path);
        }

        /// <summary>Whether any of the supported sound players is installed.</summary>
        public static bool IsAvailable => FindPlayer() != null;

        private static void Play(ref Process? process, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || FindPlayer() is not { } player) return;
            try
            {
                // Restart rather than overlap, so quick hovers or clicks retrigger the sound
                if (process is { HasExited: false }) process.Kill();
                process?.Dispose();

                var info = new ProcessStartInfo(player.Path)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (var arg in player.Args) info.ArgumentList.Add(arg);
                info.ArgumentList.Add(path);
                process = Process.Start(info);
            }
            catch
            {
                process = null; // A broken file or missing player never crashes the widget
            }
        }

        private static (string Path, string[] Args)? FindPlayer()
        {
            if (_searched) return _player;
            _searched = true;
            var folders = (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries);
            foreach (var (tool, args) in Players)
            {
                string? found = folders.Select(f => Path.Combine(f, tool)).FirstOrDefault(File.Exists);
                if (found != null) return _player = (found, args);
            }
            return null;
        }
    }
}
