using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace GTasksBar
{
    public partial class StickyNoteWindow : FluentWindow
    {
        public StickyNoteWindow()
        {
            InitializeComponent();
        }

        // This allows you to drag the borderless sticky note around the screen
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
    }
}