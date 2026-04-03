using System.Windows;
using Wpf.Ui.Controls;

namespace GTasksBar
{
    public partial class SystemTrayWindow : FluentWindow
    {
        public SystemTrayWindow()
        {
            InitializeComponent();
        }

        // Left-clicking the icon opens the main task list
        private void TaskbarIcon_LeftClick(object sender, RoutedEventArgs e)
        {
            var flyout = new TasksFlyoutWindow();
            flyout.Show();
        }

        // Right-click menu option to open the task list
        private void OpenFlyout_Click(object sender, RoutedEventArgs e)
        {
            var flyout = new TasksFlyoutWindow();
            flyout.Show();
        }

        // Right-click menu option to spawn a sticky note
        private void OpenStickyNote_Click(object sender, RoutedEventArgs e)
        {
            var note = new StickyNoteWindow();
            note.Show();
        }

        // Right-click menu option to safely shut down the background app
        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}