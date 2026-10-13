using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TasksBar
{
    public partial class BehaviorPage : Page
    {
        private bool _isLoaded = false;

        public BehaviorPage()
        {
            InitializeComponent();
            GoogleSyncToggle.IsChecked = AppConfig.Settings.EnableGoogleSync;
            StartupToggle.IsChecked = AppConfig.Settings.LaunchOnStartup;

            // 1. Load the visual state of the toggle
            StreamDeckToggle.IsChecked = AppConfig.Settings.EnableStreamDeck;

            NoteSyncToggle.IsChecked = AppConfig.Settings.SyncStickyNotes;
            HoverSoundToggle.IsChecked = AppConfig.Settings.EnableHoverSound;
            ClickSoundToggle.IsChecked = AppConfig.Settings.EnableClickSound;
            UpdateSoundFileLabels();

            _isLoaded = true;
        }

        private void UpdateSoundFileLabels()
        {
            HoverSoundFileText.Text = DescribeSoundFile(AppConfig.Settings.HoverSoundPath);
            HoverSoundFileText.ToolTip = AppConfig.Settings.HoverSoundPath;
            ClickSoundFileText.Text = DescribeSoundFile(AppConfig.Settings.ClickSoundPath);
            ClickSoundFileText.ToolTip = AppConfig.Settings.ClickSoundPath;
        }

        private static string DescribeSoundFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "No file selected";
            string name = System.IO.Path.GetFileName(path);
            return System.IO.File.Exists(path) ? name : $"{name} (file not found)";
        }

        private static string? PickMp3File(string currentPath)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a sound",
                Filter = "MP3 files (*.mp3)|*.mp3",
                CheckFileExists = true
            };

            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                string? folder = System.IO.Path.GetDirectoryName(currentPath);
                if (folder != null && System.IO.Directory.Exists(folder))
                    dialog.InitialDirectory = folder;
            }

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void BrowseHoverSound_Click(object sender, RoutedEventArgs e)
        {
            string? file = PickMp3File(AppConfig.Settings.HoverSoundPath);
            if (file == null) return;

            AppConfig.Settings.HoverSoundPath = file;
            // Picking a file implies the user wants to hear it
            AppConfig.Settings.EnableHoverSound = true;
            HoverSoundToggle.IsChecked = true;
            UpdateSoundFileLabels();
            AppConfig.Save();
        }

        private void BrowseClickSound_Click(object sender, RoutedEventArgs e)
        {
            string? file = PickMp3File(AppConfig.Settings.ClickSoundPath);
            if (file == null) return;

            AppConfig.Settings.ClickSoundPath = file;
            AppConfig.Settings.EnableClickSound = true;
            ClickSoundToggle.IsChecked = true;
            UpdateSoundFileLabels();
            AppConfig.Save();
        }

        private void PreviewHoverSound_Click(object sender, RoutedEventArgs e)
        {
            Services.TaskSoundPlayer.Preview(AppConfig.Settings.HoverSoundPath);
        }

        private void PreviewClickSound_Click(object sender, RoutedEventArgs e)
        {
            Services.TaskSoundPlayer.Preview(AppConfig.Settings.ClickSoundPath);
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (GoogleSyncToggle.IsChecked == true && !AppConfig.Settings.EnableGoogleSync ||
                GoogleSyncToggle.IsChecked == false && AppConfig.Settings.EnableGoogleSync)
            {
                AppConfig.Settings.SelectedListId = "@default";
            }
            if (!_isLoaded) return;

            bool wasNoteSync = Services.StickyNoteSync.IsEnabled;

            AppConfig.Settings.EnableGoogleSync = GoogleSyncToggle.IsChecked == true;
            AppConfig.Settings.LaunchOnStartup = StartupToggle.IsChecked == true;
            AppConfig.Settings.SyncStickyNotes = NoteSyncToggle.IsChecked == true;
            AppConfig.Settings.EnableHoverSound = HoverSoundToggle.IsChecked == true;
            AppConfig.Settings.EnableClickSound = ClickSoundToggle.IsChecked == true;

            // 2. Instantly start or stop the background server when clicked
            bool enableStreamDeck = StreamDeckToggle.IsChecked == true;
            if (AppConfig.Settings.EnableStreamDeck != enableStreamDeck)
            {
                AppConfig.Settings.EnableStreamDeck = enableStreamDeck;

                if (enableStreamDeck)
                    Services.StreamDeckServer.Instance?.Start();
                else
                    Services.StreamDeckServer.Instance?.Stop();
            }

            ApplyStartupSetting(AppConfig.Settings.LaunchOnStartup);
            AppConfig.Save();

            // Upload existing notes right away when note sync is switched on
            if (!wasNoteSync && Services.StickyNoteSync.IsEnabled)
                _ = Services.StickyNoteSync.SyncAsync();
        }

        private void Hyperlink_RequestNavigate(object sender,
                                     System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                // Must use ProcessStartInfo with UseShellExecute set to true for URLs
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);

                // Mark the event as handled so WPF doesn't try to process it further
                e.Handled = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not open link: {ex.Message}");
            }
        }
        private void ApplyStartupSetting(bool enableStartup)
        {
            try
            {
                // The standard folder in the Windows Registry where startup apps live
                string registryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                string appName = "TasksBar";

                // Gets the exact hard drive path of your currently running .exe
                string exePath = Environment.ProcessPath;

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryKeyPath, true))
                {
                    if (key != null)
                    {
                        if (enableStartup)
                        {
                            // Put quotes around the path just in case there are spaces in the folder names
                            key.SetValue(appName, $"\"{exePath}\"");
                        }
                        else
                        {
                            // If they turned it off, delete the key so it stops booting
                            if (key.GetValue(appName) != null)
                            {
                                key.DeleteValue(appName, false);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // If their antivirus blocks the registry edit, show a warning
                System.Windows.MessageBox.Show($"Could not update Startup settings: {ex.Message}");
            }
        }

        private async void LoginGoogle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var btn = sender as Wpf.Ui.Controls.Button;
                if (btn != null) btn.IsEnabled = false;

                await GoogleAuthManager.LoginAsync();

                Wpf.Ui.Controls.MessageBox messageBox = new()
                {
                    Title = "Success",
                    Content = "Successfully connected to your Google Account!",
                    CloseButtonText = "Awesome"
                };
                await messageBox.ShowDialogAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Login failed. Ensure credentials.json is in your build folder.\n\nError: {ex.Message}");
            }
            finally
            {
                var btn = sender as Wpf.Ui.Controls.Button;
                if (btn != null) btn.IsEnabled = true;
            }
        }
    }
}