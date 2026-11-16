using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TasksBar.Linux.Views
{
    /// <summary>Invisible strips along a borderless window's edges and corners that resize it, like a normal frame.</summary>
    public static class WindowEdges
    {
        public static void Attach(Window window, Panel host)
        {
            Add(window, host, WindowEdge.West, 5, double.NaN, HorizontalAlignment.Left, VerticalAlignment.Stretch, StandardCursorType.LeftSide);
            Add(window, host, WindowEdge.East, 5, double.NaN, HorizontalAlignment.Right, VerticalAlignment.Stretch, StandardCursorType.RightSide);
            Add(window, host, WindowEdge.North, double.NaN, 5, HorizontalAlignment.Stretch, VerticalAlignment.Top, StandardCursorType.TopSide);
            Add(window, host, WindowEdge.South, double.NaN, 5, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, StandardCursorType.BottomSide);
            Add(window, host, WindowEdge.NorthWest, 10, 10, HorizontalAlignment.Left, VerticalAlignment.Top, StandardCursorType.TopLeftCorner);
            Add(window, host, WindowEdge.NorthEast, 10, 10, HorizontalAlignment.Right, VerticalAlignment.Top, StandardCursorType.TopRightCorner);
            Add(window, host, WindowEdge.SouthWest, 10, 10, HorizontalAlignment.Left, VerticalAlignment.Bottom, StandardCursorType.BottomLeftCorner);
            Add(window, host, WindowEdge.SouthEast, 10, 10, HorizontalAlignment.Right, VerticalAlignment.Bottom, StandardCursorType.BottomRightCorner);
        }

        private static void Add(Window window, Panel host, WindowEdge edge, double width, double height,
                                HorizontalAlignment horizontal, VerticalAlignment vertical, StandardCursorType cursor)
        {
            var strip = new Border
            {
                Width = width,
                Height = height,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Background = Brushes.Transparent,
                Cursor = new Cursor(cursor)
            };
            strip.PointerPressed += (s, e) =>
            {
                if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed || window.WindowState != WindowState.Normal) return;
                e.Handled = true;
                window.BeginResizeDrag(edge, e);
            };
            window.GetObservable(Window.WindowStateProperty).Subscribe(new Observer(state => strip.IsVisible = state == WindowState.Normal));
            host.Children.Add(strip);
        }

        private sealed class Observer : System.IObserver<WindowState>
        {
            private readonly System.Action<WindowState> _next;
            public Observer(System.Action<WindowState> next) => _next = next;
            public void OnNext(WindowState value) => _next(value);
            public void OnError(System.Exception error) { }
            public void OnCompleted() { }
        }
    }
}
