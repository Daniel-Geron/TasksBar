using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace TasksBar
{
    public partial class StickyNoteWindow : FluentWindow
    {
        public StickyNoteWindow()
        {
            InitializeComponent();

            // 1. Apply the saved theme (Light/Dark/System)
            if (AppConfig.Settings.AppTheme == 1)
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            else if (AppConfig.Settings.AppTheme == 2)
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            else
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
                Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
            }

            Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();

            // 2. Apply the saved visual backdrop setting (Mica/Acrylic)
            this.WindowBackdropType = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allows the user to drag the note by clicking anywhere on the background
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Close and destroy the note window
            this.Close();
        }
    }
}