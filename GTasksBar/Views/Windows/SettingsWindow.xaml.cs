using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace GTasksBar
{
    public partial class SettingsWindow : FluentWindow
    {
        public SettingsWindow()
        {
            InitializeComponent();

            this.WindowBackdropType = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;

            // This safely selects the first item and loads the page
            SidebarList.SelectedIndex = 0;
        }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Simple, foolproof navigation based on which item is clicked
            if (SidebarList.SelectedIndex == 0)
            {
                RootFrame.Navigate(new AppearancePage());
            }
            else if (SidebarList.SelectedIndex == 1)
            {
                RootFrame.Navigate(new BehaviorPage());
            }
        }
    }
}