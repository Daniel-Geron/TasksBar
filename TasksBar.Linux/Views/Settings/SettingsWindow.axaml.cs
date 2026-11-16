using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentIcons.Common;

namespace TasksBar.Linux.Views.Settings
{
    /// <summary>The Settings window, ported from the Windows app: a sidebar with four pages.</summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            Backdrop.Attach(this, Chrome);
            WindowEdges.Attach(this, Frame);

            TitleBar.PointerPressed += TitleBar_PointerPressed;
            this.GetObservable(WindowStateProperty).Subscribe(new StateObserver(this));

            SidebarList.SelectedIndex = 0;
        }

        private void SidebarList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            PageHost.Content = SidebarList.SelectedIndex switch
            {
                1 => new BehaviorPage(),
                2 => new NotesPage(),
                3 => new ListsPage(),
                _ => new AppearancePage()
            };
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            BeginMoveDrag(e);
        }

        private void ToggleMaximize() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();

        private sealed class StateObserver : System.IObserver<WindowState>
        {
            private readonly SettingsWindow _window;
            public StateObserver(SettingsWindow window) => _window = window;

            public void OnNext(WindowState state)
            {
                bool maximized = state == WindowState.Maximized;
                _window.MaximizeIcon.Symbol = maximized ? Symbol.SquareMultiple : Symbol.Maximize;
                _window.Chrome.CornerRadius = new Avalonia.CornerRadius(maximized ? 0 : 8);
            }

            public void OnError(System.Exception error) { }
            public void OnCompleted() { }
        }
    }
}
