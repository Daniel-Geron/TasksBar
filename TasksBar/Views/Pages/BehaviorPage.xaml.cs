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
            LoadSyncSettings();
            StartupToggle.IsChecked = AppConfig.Settings.LaunchOnStartup;

            // 1. Load the visual state of the toggle
            StreamDeckToggle.IsChecked = AppConfig.Settings.EnableStreamDeck;

            NoteSyncToggle.IsChecked = AppConfig.Settings.SyncStickyNotes;
            HoverSoundToggle.IsChecked = AppConfig.Settings.EnableHoverSound;
            ClickSoundToggle.IsChecked = AppConfig.Settings.EnableClickSound;
            UpdateSoundFileLabels();
            UpdateHotkeyBoxes();
            // Never leave the shortcuts suspended if settings closes mid-recording
            Unloaded += (s, e) => Services.HotkeyManager.Apply();

            _isLoaded = true;
        }

        // --- TASK SYNC (Google Tasks / Tasks.org / CalDAV) ---

        private void LoadSyncSettings()
        {
            string provider = AppConfig.Settings.SyncProvider;
            foreach (ComboBoxItem item in SyncProviderBox.Items)
            {
                if ((string)item.Tag == provider) SyncProviderBox.SelectedItem = item;
            }
            if (SyncProviderBox.SelectedItem == null) SyncProviderBox.SelectedIndex = 0;

            CalDavServerTypeBox.SelectedIndex = AppConfig.Settings.CalDavIsTasksOrg ? 0 : 1;
            CalDavServerBox.Text = AppConfig.Settings.CalDavIsTasksOrg ? "" : AppConfig.Settings.CalDavServerUrl;
            CalDavUserBox.Text = AppConfig.Settings.CalDavUsername;
            UpdateSyncPanels();
        }

        private string SelectedProvider => (SyncProviderBox.SelectedItem as ComboBoxItem)?.Tag as string ?? Services.Sync.TaskSync.Local;
        private bool IsTasksOrgSelected => CalDavServerTypeBox.SelectedIndex != 1;

        private void UpdateSyncPanels()
        {
            string provider = SelectedProvider;
            GooglePanel.Visibility = provider == Services.Sync.TaskSync.Google ? Visibility.Visible : Visibility.Collapsed;
            CalDavPanel.Visibility = provider == Services.Sync.TaskSync.CalDav ? Visibility.Visible : Visibility.Collapsed;

            bool tasksOrg = IsTasksOrgSelected;
            CalDavServerLabel.Visibility = CalDavServerBox.Visibility = tasksOrg ? Visibility.Collapsed : Visibility.Visible;
            TasksOrgHelpText.Visibility = tasksOrg ? Visibility.Visible : Visibility.Collapsed;
            CalDavHelpText.Visibility = tasksOrg ? Visibility.Collapsed : Visibility.Visible;

            bool connected = Services.Sync.TaskSync.IsCalDavConfigured;
            CalDavDisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            CalDavConnectButton.Content = connected ? "Reconnect" : "Connect";
            if (connected)
            {
                CalDavStatusText.Text = $"Connected to {Services.Sync.TaskSync.CalDavName} as {AppConfig.Settings.CalDavUsername}";
            }
            else if (!string.IsNullOrEmpty(AppConfig.Settings.CalDavUsername))
            {
                CalDavStatusText.Text = "Not connected. Enter your app password to connect again.";
            }
            else
            {
                CalDavStatusText.Text = "Not connected";
            }
        }

        private void SyncProvider_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;

            string provider = SelectedProvider;
            if (provider != AppConfig.Settings.SyncProvider)
            {
                AppConfig.Settings.SyncProvider = provider;
                // List ids belong to one service, so start from its default list
                AppConfig.Settings.SelectedListId = "@default";
                AppConfig.Save();
            }
            UpdateSyncPanels();
        }

        private void CalDavServerType_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            UpdateSyncPanels();
        }

        private async void CalDavConnect_Click(object sender, RoutedEventArgs e)
        {
            bool tasksOrg = IsTasksOrgSelected;
            string server = tasksOrg ? Services.Sync.TaskSync.TasksOrgServerUrl : CalDavServerBox.Text.Trim();
            string username = CalDavUserBox.Text.Trim();
            string password = CalDavPasswordBox.Password;

            if ((!tasksOrg && server.Length == 0) || username.Length == 0 || password.Length == 0)
            {
                CalDavStatusText.Text = tasksOrg
                    ? "Enter the username and app password from Tasks.org."
                    : "Enter the server address, username and password.";
                return;
            }

            CalDavConnectButton.IsEnabled = false;
            CalDavStatusText.Text = "Connecting...";
            try
            {
                int lists = await Services.Sync.TaskSync.SetUpCalDavAsync(server, username, password, tasksOrg);
                CalDavPasswordBox.Password = "";
                UpdateSyncPanels();
                CalDavStatusText.Text += lists == 1 ? " · 1 task list found" : $" · {lists} task lists found";
            }
            catch (Services.Sync.RemoteAuthException)
            {
                CalDavStatusText.Text = tasksOrg
                    ? "Tasks.org didn't accept that username and app password. Check them, or create a new app password."
                    : "The server didn't accept that username and password.";
            }
            catch (Exception ex)
            {
                CalDavStatusText.Text = $"Couldn't connect: {ex.Message}";
            }
            finally
            {
                CalDavConnectButton.IsEnabled = true;
            }
        }

        private void CalDavDisconnect_Click(object sender, RoutedEventArgs e)
        {
            Services.Sync.TaskSync.ForgetCalDavAccount();
            CalDavPasswordBox.Password = "";
            UpdateSyncPanels();
        }

        // --- STICKY NOTE SHORTCUTS ---

        private string GetHotkey(string which) =>
            which == "NewNote" ? AppConfig.Settings.NewNoteHotkey : AppConfig.Settings.OpenAllNotesHotkey;

        private void SetHotkey(string which, string value)
        {
            if (which == "NewNote") AppConfig.Settings.NewNoteHotkey = value;
            else AppConfig.Settings.OpenAllNotesHotkey = value;
            AppConfig.Save();
        }

        private void UpdateHotkeyBoxes()
        {
            NewNoteHotkeyBox.Text = AppConfig.Settings.NewNoteHotkey;
            OpenAllNotesHotkeyBox.Text = AppConfig.Settings.OpenAllNotesHotkey;
            ShowHotkeyStatus(NewNoteHotkeyStatus, AppConfig.Settings.NewNoteHotkey);
            ShowHotkeyStatus(OpenAllNotesHotkeyStatus, AppConfig.Settings.OpenAllNotesHotkey);
        }

        private static void ShowHotkeyStatus(TextBlock status, string shortcut)
        {
            string? problem = null;
            if (!string.IsNullOrEmpty(shortcut) &&
                AppConfig.Settings.NewNoteHotkey == AppConfig.Settings.OpenAllNotesHotkey)
                problem = "Both shortcuts are the same";
            else if (Services.HotkeyManager.FailedShortcuts.Contains(shortcut))
                problem = "Already used by Windows or another app";

            status.Text = problem ?? "";
            status.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void HotkeyBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            // Release the shortcuts so pressing an existing one gets recorded instead of firing
            Services.HotkeyManager.Suspend();
        }

        private void HotkeyBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            Services.HotkeyManager.Apply();
            UpdateHotkeyBoxes();
        }

        private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.TextBox box || box.Tag is not string which) return;
            e.Handled = true;

            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            var modifiers = System.Windows.Input.Keyboard.Modifiers;

            // Escape cancels, Backspace/Delete alone removes the shortcut
            if (modifiers == System.Windows.Input.ModifierKeys.None)
            {
                if (key == System.Windows.Input.Key.Escape)
                {
                    System.Windows.Input.Keyboard.ClearFocus();
                    return;
                }
                if (key == System.Windows.Input.Key.Back || key == System.Windows.Input.Key.Delete)
                {
                    SetHotkey(which, "");
                    System.Windows.Input.Keyboard.ClearFocus();
                    return;
                }
            }

            // Still holding only modifiers: preview what's being pressed
            if (Services.HotkeyManager.IsModifierKey(key))
            {
                box.Text = Services.HotkeyManager.Format(modifiers, System.Windows.Input.Key.None) + "+";
                return;
            }

            // A shortcut needs at least one of Ctrl/Alt/Shift/Win so normal typing isn't hijacked
            if (modifiers == System.Windows.Input.ModifierKeys.None)
            {
                box.Text = "Add Ctrl, Alt, Shift or Win";
                return;
            }

            SetHotkey(which, Services.HotkeyManager.Format(modifiers, key));
            System.Windows.Input.Keyboard.ClearFocus(); // Re-registers via LostKeyboardFocus
        }

        private void ClearHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string which)
            {
                SetHotkey(which, "");
                Services.HotkeyManager.Apply();
                UpdateHotkeyBoxes();
            }
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
            if (!_isLoaded) return;

            bool wasNoteSync = Services.StickyNoteSync.IsEnabled;

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