using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32; // THE FIX: Needed to access the Windows Registry

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
            _isLoaded = true;
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            AppConfig.Settings.EnableGoogleSync = GoogleSyncToggle.IsChecked == true;
            AppConfig.Settings.LaunchOnStartup = StartupToggle.IsChecked == true;

            // THE FIX: Actually tell Windows to enable/disable startup!
            ApplyStartupSetting(AppConfig.Settings.LaunchOnStartup);

            AppConfig.Save();
        }

        // --- NEW ADDITION: The Registry Helper ---
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