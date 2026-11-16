using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TasksBar.Linux.Views.Settings
{
    public partial class AppearancePage : UserControl
    {
        private readonly bool _isLoaded;

        public AppearancePage()
        {
            InitializeComponent();

            AcrylicToggle.IsChecked = AppConfig.Settings.UseAcrylic;
            ThemeComboBox.SelectedIndex = AppConfig.Settings.AppTheme;
            LockToggle.IsChecked = AppConfig.Settings.IsLocked;
            PositionComboBox.SelectedIndex = (int)AppConfig.Settings.Position;

            _isLoaded = true;
        }

        private void Setting_Changed(object? sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            int oldTheme = AppConfig.Settings.AppTheme;
            bool oldAcrylic = AppConfig.Settings.UseAcrylic;

            AppConfig.Settings.UseAcrylic = AcrylicToggle.IsChecked == true;
            AppConfig.Settings.AppTheme = ThemeComboBox.SelectedIndex;
            AppConfig.Settings.IsLocked = LockToggle.IsChecked == true;
            AppConfig.Settings.Position = (WidgetPosition)PositionComboBox.SelectedIndex;

            // Applied right away to every window (note drawings recolor themselves with the theme)
            if (oldTheme != AppConfig.Settings.AppTheme) (Application.Current as App)?.ApplyTheme();
            if (oldAcrylic != AppConfig.Settings.UseAcrylic) Backdrop.RefreshAll();

            AppConfig.Save();
        }
    }
}
