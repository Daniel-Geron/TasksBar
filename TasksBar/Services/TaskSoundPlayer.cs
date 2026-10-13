using System.IO;
using System.Windows.Media;

namespace TasksBar.Services
{
    /// <summary>
    /// Plays the optional hover and click sounds for the task completion circle.
    /// Players are created lazily and can be released to keep memory low while hidden.
    /// Must be used from the UI thread.
    /// </summary>
    public static class TaskSoundPlayer
    {
        private static MediaPlayer? _hoverPlayer;
        private static string? _hoverPath;

        private static MediaPlayer? _clickPlayer;
        private static string? _clickPath;

        public static void PlayHover()
        {
            if (!AppConfig.Settings.EnableHoverSound) return;
            Play(ref _hoverPlayer, ref _hoverPath, AppConfig.Settings.HoverSoundPath);
        }

        public static void PlayClick()
        {
            if (!AppConfig.Settings.EnableClickSound) return;
            Play(ref _clickPlayer, ref _clickPath, AppConfig.Settings.ClickSoundPath);
        }

        /// <summary>Plays a file once, ignoring the enabled toggles. Used for previews in settings.</summary>
        public static void Preview(string? path)
        {
            MediaPlayer? player = null;
            string? loaded = null;
            Play(ref player, ref loaded, path);
            if (player != null)
            {
                player.MediaEnded += (s, e) => player.Close();
                player.MediaFailed += (s, e) => player.Close();
            }
        }

        /// <summary>Closes both players and frees their resources. They reload on next use.</summary>
        public static void Release()
        {
            _hoverPlayer?.Close();
            _hoverPlayer = null;
            _hoverPath = null;

            _clickPlayer?.Close();
            _clickPlayer = null;
            _clickPath = null;
        }

        private static void Play(ref MediaPlayer? player, ref string? loadedPath, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            try
            {
                // Reopen only when the file changed, so repeated hovers are instant
                if (player == null || !string.Equals(loadedPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    player?.Close();
                    player = new MediaPlayer();
                    player.Open(new Uri(path, UriKind.Absolute));
                    loadedPath = path;
                }

                // Restart from the beginning so rapid hovers or clicks retrigger the sound
                player.Stop();
                player.Position = TimeSpan.Zero;
                player.Play();
            }
            catch
            {
                // A broken or unsupported file should never crash the widget
            }
        }
    }
}
