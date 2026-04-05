using System;
using System.Windows;
using System.Windows.Controls;

namespace GTasksBar
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
            AppConfig.Save();
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