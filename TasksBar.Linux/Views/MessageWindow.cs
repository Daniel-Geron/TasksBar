using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TasksBar.Linux.Views
{
    /// <summary>A small themed message box (Avalonia has none built in).</summary>
    public class MessageWindow : Window
    {
        private bool _result;

        private MessageWindow(string title, string message, string okText, string? cancelText)
        {
            Title = title;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            this.Bind(BackgroundProperty, this.GetResourceObservable("SolidBackgroundFillColorBaseBrush"));

            var ok = new Button { Content = okText, Classes = { "accent" }, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            ok.Click += (s, e) => { _result = true; Close(); };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            if (cancelText != null)
            {
                var cancel = new Button { Content = cancelText, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
                cancel.Click += (s, e) => Close();
                buttons.Children.Add(cancel);
            }
            buttons.Children.Add(ok);

            var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("TextFillColorPrimaryBrush"));

            Content = new StackPanel
            {
                Margin = new Thickness(24, 20, 24, 20),
                Spacing = 20,
                Children = { text, buttons }
            };
        }

        /// <summary>Shows a message with an OK button.</summary>
        public static Task ShowAsync(string title, string message) =>
            new MessageWindow(title, message, "OK", null).ShowAndWait();

        /// <summary>Asks a yes/no question. Returns true for yes.</summary>
        public static Task<bool> AskAsync(string title, string message, string yes = "Yes", string no = "No") =>
            new MessageWindow(title, message, yes, no).ShowAndWait();

        private Task<bool> ShowAndWait()
        {
            var done = new TaskCompletionSource<bool>();
            Closed += (s, e) => done.TrySetResult(_result);
            Show();
            Activate();
            return done.Task;
        }
    }
}
