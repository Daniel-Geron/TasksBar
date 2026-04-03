using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace GTasksBar
{
    public partial class SettingsWindow : FluentWindow
    {
        private bool _isLoaded = false;

        public SettingsWindow()
        {
            InitializeComponent();

            // THE FIX: Removed SystemThemeWatcher so it doesn't fight the main window for the accent color!

            // THE FIX: Match the backdrop type of the main window so the backgrounds look identical
            this.WindowBackdropType = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;

            // Load current settings into the UI
            AcrylicToggle.IsChecked = AppConfig.Settings.UseAcrylic;
            GoogleSyncToggle.IsChecked = AppConfig.Settings.EnableGoogleSync;
            LockToggle.IsChecked = AppConfig.Settings.IsLocked;
            PositionComboBox.SelectedIndex = (int)AppConfig.Settings.Position;
            ShowCompletedToggle.IsChecked = AppConfig.Settings.ShowCompletedTasks;
            StartupToggle.IsChecked = AppConfig.Settings.LaunchOnStartup;
            ThemeComboBox.SelectedIndex = AppConfig.Settings.AppTheme;

            _isLoaded = true;
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            int oldTheme = AppConfig.Settings.AppTheme;
            bool oldAcrylic = AppConfig.Settings.UseAcrylic;

            AppConfig.Settings.UseAcrylic = AcrylicToggle.IsChecked == true;
            AppConfig.Settings.EnableGoogleSync = GoogleSyncToggle.IsChecked == true;
            AppConfig.Settings.IsLocked = LockToggle.IsChecked == true;
            AppConfig.Settings.Position = (WidgetPosition)PositionComboBox.SelectedIndex;
            AppConfig.Settings.ShowCompletedTasks = ShowCompletedToggle.IsChecked == true;
            AppConfig.Settings.LaunchOnStartup = StartupToggle.IsChecked == true;
            AppConfig.Settings.AppTheme = ThemeComboBox.SelectedIndex;

            // 1. Apply the internal WPF Theme
            if (oldTheme != AppConfig.Settings.AppTheme)
            {
                if (AppConfig.Settings.AppTheme == 1) ApplicationThemeManager.Apply(ApplicationTheme.Light);
                else if (AppConfig.Settings.AppTheme == 2) ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                else ApplicationThemeManager.ApplySystemTheme();
            }

            // 2. THE FIX: Force ALL open windows to instantly redraw their OS-level backgrounds
            if (oldTheme != AppConfig.Settings.AppTheme || oldAcrylic != AppConfig.Settings.UseAcrylic)
            {
                var newBackdrop = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;

                foreach (Window window in Application.Current.Windows)
                {
                    if (window is FluentWindow fluentWindow)
                    {
                        // Toggling the backdrop off and on forces Windows to instantly repaint the light/dark frame
                        fluentWindow.WindowBackdropType = WindowBackdropType.None;
                        fluentWindow.WindowBackdropType = newBackdrop;
                    }
                }
            }

            AppConfig.Save();
        }
    }
}