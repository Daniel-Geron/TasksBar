using System.Windows;
using Wpf.Ui.Controls;

namespace GTasksBar
{
    public partial class SettingsWindow : FluentWindow
    {
        public SettingsWindow()
        {
            InitializeComponent();

            // Tell the Settings window to ALSO use the global accent color!
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

            // Load current settings into the UI
            AcrylicToggle.IsChecked = AppConfig.Settings.UseAcrylic;
            TopmostToggle.IsChecked = AppConfig.Settings.StayOnTop;
            GoogleSyncToggle.IsChecked = AppConfig.Settings.EnableGoogleSync;
            LockToggle.IsChecked = AppConfig.Settings.IsLocked;
            PositionComboBox.SelectedIndex = (int)AppConfig.Settings.Position;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Update the settings object
            AppConfig.Settings.UseAcrylic = AcrylicToggle.IsChecked == true;
            AppConfig.Settings.StayOnTop = TopmostToggle.IsChecked == true;
            AppConfig.Settings.EnableGoogleSync = GoogleSyncToggle.IsChecked == true;
            AppConfig.Settings.IsLocked = LockToggle.IsChecked == true;
            AppConfig.Settings.Position = (WidgetPosition)PositionComboBox.SelectedIndex;

            // Write it to the hard drive!
            AppConfig.Save();

            this.Close();
        }
    }
}