using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TasksBar.Linux.Services;
using TasksBar.Services;
using TasksBar.Services.Sync;

namespace TasksBar.Linux.Views.Settings
{
    public partial class BehaviorPage : UserControl
    {
        private readonly bool _isLoaded;

        public BehaviorPage()
        {
            InitializeComponent();
            LoadSyncSettings();

            StartupToggle.IsChecked = AppConfig.Settings.LaunchOnStartup;
            NoteSyncToggle.IsChecked = AppConfig.Settings.SyncStickyNotes;
            HoverSoundToggle.IsChecked = AppConfig.Settings.EnableHoverSound;
            ClickSoundToggle.IsChecked = AppConfig.Settings.EnableClickSound;
            UpdateSoundLabels();
            SetUpShortcuts();

            // Never leave the shortcuts released if Settings closes mid-recording
            DetachedFromVisualTree += (s, e) => GlobalShortcuts.Apply();

            _isLoaded = true;
        }

        // ------------------------------------------------------------------ task sync

        private void LoadSyncSettings()
        {
            string provider = AppConfig.Settings.SyncProvider;
            SyncProviderBox.SelectedItem = SyncProviderBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == provider);
            if (SyncProviderBox.SelectedItem == null) SyncProviderBox.SelectedIndex = 0;

            CalDavServerTypeBox.SelectedIndex = AppConfig.Settings.CalDavIsTasksOrg ? 0 : 1;
            CalDavServerBox.Text = AppConfig.Settings.CalDavIsTasksOrg ? "" : AppConfig.Settings.CalDavServerUrl;
            CalDavUserBox.Text = AppConfig.Settings.CalDavUsername;
            UpdateSyncPanels();
        }

        private string SelectedProvider => (SyncProviderBox.SelectedItem as ComboBoxItem)?.Tag as string ?? TaskSync.Local;
        private bool IsTasksOrgSelected => CalDavServerTypeBox.SelectedIndex != 1;

        private void UpdateSyncPanels()
        {
            string provider = SelectedProvider;
            GooglePanel.IsVisible = provider == TaskSync.Google;
            CalDavPanel.IsVisible = provider == TaskSync.CalDav;

            bool tasksOrg = IsTasksOrgSelected;
            CalDavServerLabel.IsVisible = CalDavServerBox.IsVisible = !tasksOrg;
            TasksOrgHelp.IsVisible = tasksOrg;
            CalDavHelp.IsVisible = !tasksOrg;

            bool connected = TaskSync.IsCalDavConfigured;
            CalDavDisconnectButton.IsVisible = connected;
            CalDavConnectText.Text = connected ? "Reconnect" : "Connect";
            if (connected)
                CalDavStatusText.Text = $"Connected to {TaskSync.CalDavName} as {AppConfig.Settings.CalDavUsername}";
            else if (!string.IsNullOrEmpty(AppConfig.Settings.CalDavUsername))
                CalDavStatusText.Text = "Not connected. Enter your app password to connect again.";
            else
                CalDavStatusText.Text = "Not connected";
        }

        private void SyncProvider_Changed(object? sender, SelectionChangedEventArgs e)
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

        private void CalDavServerType_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded) UpdateSyncPanels();
        }

        private async void CalDavConnect_Click(object? sender, RoutedEventArgs e)
        {
            bool tasksOrg = IsTasksOrgSelected;
            string server = tasksOrg ? TaskSync.TasksOrgServerUrl : (CalDavServerBox.Text ?? "").Trim();
            string username = (CalDavUserBox.Text ?? "").Trim();
            string password = CalDavPasswordBox.Text ?? "";

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
                int lists = await TaskSync.SetUpCalDavAsync(server, username, password, tasksOrg);
                CalDavPasswordBox.Text = "";
                UpdateSyncPanels();
                CalDavStatusText.Text += lists == 1 ? " · 1 task list found" : $" · {lists} task lists found";
            }
            catch (RemoteAuthException)
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

        private void CalDavDisconnect_Click(object? sender, RoutedEventArgs e)
        {
            TaskSync.ForgetCalDavAccount();
            CalDavPasswordBox.Text = "";
            UpdateSyncPanels();
        }

        private async void LoginGoogle_Click(object? sender, RoutedEventArgs e)
        {
            if (!AppPaths.HasCredentials)
            {
                await MessageWindow.ShowAsync("Missing Credentials",
                    "To connect to Google, you need to provide your own API credentials.\n\n" +
                    $"Place your 'credentials.json' file in {AppPaths.DataDirectory} and try again.");
                return;
            }

            GoogleLoginButton.IsEnabled = false;
            try
            {
                // Opens the sign-in page in your browser
                await GoogleAuthManager.LoginAsync();
                await MessageWindow.ShowAsync("Success", "Successfully connected to your Google Account!");
            }
            catch (Exception ex)
            {
                await MessageWindow.ShowAsync("TasksBar", $"Login failed.\n\nError: {ex.Message}");
            }
            finally
            {
                GoogleLoginButton.IsEnabled = true;
            }
        }

        // ------------------------------------------------------------------ sticky note shortcuts

        private void SetUpShortcuts()
        {
            bool manual = GlobalShortcuts.Current == GlobalShortcuts.Method.Manual;
            NewNoteCard.IsVisible = OpenAllCard.IsVisible = !manual;
            ManualShortcutsPanel.IsVisible = manual;
            NewNoteCommandBox.Text = GlobalShortcuts.CommandFor(InstanceChannel.NewNote);
            OpenAllCommandBox.Text = GlobalShortcuts.CommandFor(InstanceChannel.OpenNotes);
            if (manual) return;

            if (GlobalShortcuts.Current == GlobalShortcuts.Method.Gnome)
            {
                NewNoteCard.Description = "Works from any app (added to GNOME's keyboard shortcuts)";
                OpenAllCard.Description = "Shows every note on top (added to GNOME's keyboard shortcuts)";
            }

            foreach (var box in new[] { NewNoteHotkeyBox, OpenAllNotesHotkeyBox })
            {
                box.AddHandler(KeyDownEvent, HotkeyBox_KeyDown, RoutingStrategies.Tunnel);
                // Release the shortcuts so pressing an existing one gets recorded instead of firing
                box.GotFocus += (s, e) => GlobalShortcuts.Suspend();
                box.LostFocus += (s, e) =>
                {
                    GlobalShortcuts.Apply();
                    UpdateHotkeyBoxes();
                };
            }
            UpdateHotkeyBoxes();
        }

        private static string GetHotkey(string which) =>
            which == "NewNote" ? AppConfig.Settings.NewNoteHotkey : AppConfig.Settings.OpenAllNotesHotkey;

        private static void SetHotkey(string which, string value)
        {
            if (which == "NewNote") AppConfig.Settings.NewNoteHotkey = value;
            else AppConfig.Settings.OpenAllNotesHotkey = value;
            AppConfig.Save();
        }

        private void UpdateHotkeyBoxes()
        {
            NewNoteHotkeyBox.Text = AppConfig.Settings.NewNoteHotkey;
            OpenAllNotesHotkeyBox.Text = AppConfig.Settings.OpenAllNotesHotkey;
            NewNoteCard.Status = HotkeyProblem(AppConfig.Settings.NewNoteHotkey);
            OpenAllCard.Status = HotkeyProblem(AppConfig.Settings.OpenAllNotesHotkey);
        }

        private static string? HotkeyProblem(string shortcut)
        {
            if (string.IsNullOrEmpty(shortcut)) return null;
            if (AppConfig.Settings.NewNoteHotkey == AppConfig.Settings.OpenAllNotesHotkey) return "Both shortcuts are the same";
            if (GlobalShortcuts.FailedShortcuts.Contains(shortcut)) return "Already used by your desktop or another app";
            return null;
        }

        private void ClearFocus() => TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();

        private void HotkeyBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox box || box.Tag is not string which) return;
            e.Handled = true;

            var modifiers = GlobalShortcuts.FromAvalonia(e.KeyModifiers);

            // Escape cancels, Backspace/Delete alone removes the shortcut
            if (modifiers == GlobalShortcuts.Modifiers.None)
            {
                if (e.Key == Key.Escape)
                {
                    ClearFocus();
                    return;
                }
                if (e.Key == Key.Back || e.Key == Key.Delete)
                {
                    SetHotkey(which, "");
                    ClearFocus();
                    return;
                }
            }

            // Still holding only modifiers: preview what's being pressed
            if (GlobalShortcuts.IsModifierKey(e.Key))
            {
                box.Text = GlobalShortcuts.Format(modifiers, null) + "+";
                return;
            }

            // A shortcut needs at least one of Ctrl/Alt/Shift/Super so normal typing isn't hijacked
            if (modifiers == GlobalShortcuts.Modifiers.None)
            {
                box.Text = "Add Ctrl, Alt, Shift or Super";
                return;
            }

            string? key = GlobalShortcuts.KeyName(e.Key);
            if (key == null)
            {
                box.Text = "Use a letter, number or F-key";
                return;
            }

            SetHotkey(which, GlobalShortcuts.Format(modifiers, key));
            ClearFocus(); // Re-registers on LostFocus
        }

        private void ClearHotkey_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control { Tag: string which })
            {
                SetHotkey(which, "");
                GlobalShortcuts.Apply();
                UpdateHotkeyBoxes();
            }
        }

        // ------------------------------------------------------------------ sounds

        private void UpdateSoundLabels()
        {
            HoverSoundCard.Description = "Plays when hovering a task's check circle\n" + DescribeSoundFile(AppConfig.Settings.HoverSoundPath);
            ClickSoundCard.Description = "Plays when clicking a task's check circle\n" + DescribeSoundFile(AppConfig.Settings.ClickSoundPath);
            string? missing = TaskSounds.IsAvailable ? null : "No sound player found (install pipewire-bin or pulseaudio-utils)";
            HoverSoundCard.Status = missing;
            ClickSoundCard.Status = missing;
        }

        private static string DescribeSoundFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "No file selected";
            string name = Path.GetFileName(path);
            return File.Exists(path) ? name : $"{name} (file not found)";
        }

        private async System.Threading.Tasks.Task<string?> PickSoundFileAsync(string currentPath)
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null || !storage.CanOpen) return null;
            try
            {
                IStorageFolder? start = null;
                string? folder = Path.GetDirectoryName(currentPath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) start = await storage.TryGetFolderFromPathAsync(folder);

                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose a sound",
                    AllowMultiple = false,
                    SuggestedStartLocation = start,
                    FileTypeFilter = new[] { new FilePickerFileType("Sounds") { Patterns = new[] { "*.mp3", "*.wav", "*.ogg", "*.oga", "*.flac", "*.opus" } } }
                });
                return files.FirstOrDefault()?.TryGetLocalPath();
            }
            catch (Exception ex)
            {
                await MessageWindow.ShowAsync("TasksBar", $"Couldn't open the file picker: {ex.Message}");
                return null;
            }
        }

        private async void BrowseHoverSound_Click(object? sender, RoutedEventArgs e)
        {
            string? file = await PickSoundFileAsync(AppConfig.Settings.HoverSoundPath);
            if (file == null) return;
            AppConfig.Settings.HoverSoundPath = file;
            // Picking a file implies wanting to hear it
            AppConfig.Settings.EnableHoverSound = true;
            HoverSoundToggle.IsChecked = true;
            UpdateSoundLabels();
            AppConfig.Save();
        }

        private async void BrowseClickSound_Click(object? sender, RoutedEventArgs e)
        {
            string? file = await PickSoundFileAsync(AppConfig.Settings.ClickSoundPath);
            if (file == null) return;
            AppConfig.Settings.ClickSoundPath = file;
            AppConfig.Settings.EnableClickSound = true;
            ClickSoundToggle.IsChecked = true;
            UpdateSoundLabels();
            AppConfig.Save();
        }

        private void PreviewHoverSound_Click(object? sender, RoutedEventArgs e) => TaskSounds.Preview(AppConfig.Settings.HoverSoundPath);

        private void PreviewClickSound_Click(object? sender, RoutedEventArgs e) => TaskSounds.Preview(AppConfig.Settings.ClickSoundPath);

        // ------------------------------------------------------------------ switches

        private async void Setting_Changed(object? sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            bool wasNoteSync = StickyNoteSync.IsEnabled;
            bool startup = StartupToggle.IsChecked == true;

            AppConfig.Settings.SyncStickyNotes = NoteSyncToggle.IsChecked == true;
            AppConfig.Settings.EnableHoverSound = HoverSoundToggle.IsChecked == true;
            AppConfig.Settings.EnableClickSound = ClickSoundToggle.IsChecked == true;

            if (startup != AppConfig.Settings.LaunchOnStartup)
            {
                AppConfig.Settings.LaunchOnStartup = startup;
                if (Autostart.Apply(startup) is { } error)
                    await MessageWindow.ShowAsync("TasksBar", $"Could not update the startup setting: {error}");
            }
            AppConfig.Save();

            // Upload existing notes right away when note sync is switched on
            if (!wasNoteSync && StickyNoteSync.IsEnabled) _ = StickyNoteSync.SyncAsync();
        }
    }
}
