using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TasksBar
{
    public partial class AppearancePage : Page
    {
        private bool _isLoaded = false;

        public AppearancePage()
        {
            InitializeComponent();

            // Load visual settings
            AcrylicToggle.IsChecked = AppConfig.Settings.UseAcrylic;
            ThemeComboBox.SelectedIndex = AppConfig.Settings.AppTheme;

            // Load layout settings
            LockToggle.IsChecked = AppConfig.Settings.IsLocked;
            PositionComboBox.SelectedIndex = (int)AppConfig.Settings.Position;

            _isLoaded = true;
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            int oldTheme = AppConfig.Settings.AppTheme;
            bool oldAcrylic = AppConfig.Settings.UseAcrylic;

            // Save visual settings
            AppConfig.Settings.UseAcrylic = AcrylicToggle.IsChecked == true;
            AppConfig.Settings.AppTheme = ThemeComboBox.SelectedIndex;

            // Save layout settings
            AppConfig.Settings.IsLocked = LockToggle.IsChecked == true;
            AppConfig.Settings.Position = (WidgetPosition)PositionComboBox.SelectedIndex;

            // Apply Theme instantly
            if (oldTheme != AppConfig.Settings.AppTheme)
            {
                if (AppConfig.Settings.AppTheme == 1) ApplicationThemeManager.Apply(ApplicationTheme.Light);
                else if (AppConfig.Settings.AppTheme == 2) ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                else ApplicationThemeManager.ApplySystemTheme();
            }

            // Apply Backdrop instantly
            if (oldTheme != AppConfig.Settings.AppTheme || oldAcrylic != AppConfig.Settings.UseAcrylic)
            {
                var newBackdrop = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is FluentWindow fluentWindow)
                    {
                        fluentWindow.WindowBackdropType = WindowBackdropType.None;
                        fluentWindow.WindowBackdropType = newBackdrop;
                    }
                }
            }

            AppConfig.Save();
        }
    }
}