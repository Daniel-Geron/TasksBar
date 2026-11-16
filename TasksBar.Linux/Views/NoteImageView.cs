using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using TasksBar.Linux.Services;

namespace TasksBar.Linux.Views
{
    /// <summary>
    /// One pasted image floating on a sticky note, ported from the Windows app. Drag it to move, drag a
    /// corner handle to resize (keeps the aspect ratio and pins the opposite corner), drag the round
    /// handle to rotate (Shift snaps to 15°), and use the ✕ or right-click menu to remove it.
    /// </summary>
    public class NoteImageView : Grid
    {
        private const double MinImageWidth = 40;
        private const double SnapDegrees = 15;

        public NoteImage Model { get; }

        private readonly Action _changed;
        private readonly Action<NoteImageView> _delete;
        private readonly Bitmap? _bitmap;
        private readonly RotateTransform _rotation;
        private readonly Border _outline;
        private readonly Border _deleteButton;
        private readonly Border _rotateHandle;
        private readonly Border[] _cornerHandles;

        private enum DragMode { None, Move, Resize, Rotate }
        private DragMode _mode = DragMode.None;
        private Point _dragStart;
        private double _startX, _startY, _startWidth, _startHeight, _startAngle, _startMouseAngle;
        private bool _dragged;

        private int _cornerSignX, _cornerSignY;
        private Point _pinnedCorner;

        public NoteImageView(NoteImage model, Action changed, Action<NoteImageView> delete)
        {
            Model = model;
            _changed = changed;
            _delete = delete;

            Width = model.Width;
            Height = model.Height;
            Canvas.SetLeft(this, model.X);
            Canvas.SetTop(this, model.Y);
            Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.SizeAll);

            // Rotation turns the image around its center; X/Y stay the unrotated top-left corner
            _rotation = new RotateTransform(model.Rotation);
            RenderTransformOrigin = RelativePoint.Center;
            RenderTransform = _rotation;

            _bitmap = NoteImageBitmaps.Load(model.Id);
            Children.Add(_bitmap != null
                ? new Image { Source = _bitmap, Stretch = Stretch.Fill }
                : CreatePlaceholder());

            _outline = new Border { BorderThickness = new Thickness(1.5), IsHitTestVisible = false, IsVisible = false };
            _outline.Bind(Border.BorderBrushProperty, _outline.GetResourceObservable("AccentFillColorDefaultBrush"));
            Children.Add(_outline);

            _deleteButton = RoundButton(Symbol.Dismiss, 12, new Thickness(0, 10, 10, 0), HorizontalAlignment.Right, "Remove image");
            _deleteButton.Background = new SolidColorBrush(Color.FromArgb(200, 32, 32, 32));
            _deleteButton.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                _delete(this);
            };
            Children.Add(_deleteButton);

            // Inside the image: the note clips anything above it, and images often sit at the top
            _rotateHandle = RoundButton(Symbol.ArrowRotateClockwise, 14, new Thickness(0, 10, 0, 0), HorizontalAlignment.Center, "Rotate (hold Shift to snap)");
            _rotateHandle.Bind(Border.BackgroundProperty, _rotateHandle.GetResourceObservable("AccentFillColorDefaultBrush"));
            _rotateHandle.PointerPressed += (s, e) => BeginDrag(DragMode.Rotate, e);
            Children.Add(_rotateHandle);

            _cornerHandles = new[]
            {
                CreateCornerHandle(-1, -1, HorizontalAlignment.Left, VerticalAlignment.Top, StandardCursorType.TopLeftCorner),
                CreateCornerHandle(1, -1, HorizontalAlignment.Right, VerticalAlignment.Top, StandardCursorType.TopRightCorner),
                CreateCornerHandle(-1, 1, HorizontalAlignment.Left, VerticalAlignment.Bottom, StandardCursorType.BottomLeftCorner),
                CreateCornerHandle(1, 1, HorizontalAlignment.Right, VerticalAlignment.Bottom, StandardCursorType.BottomRightCorner)
            };
            foreach (var handle in _cornerHandles) Children.Add(handle);

            PointerPressed += (s, e) =>
            {
                if (e.Handled) return;
                var point = e.GetCurrentPoint(this);
                if (point.Properties.IsLeftButtonPressed) BeginDrag(DragMode.Move, e);
            };
            PointerMoved += OnPointerMoved;
            PointerReleased += (s, e) =>
            {
                if (_mode == DragMode.None) return;
                e.Pointer.Capture(null);
                EndDrag();
            };
            PointerCaptureLost += (s, e) => EndDrag();
            PointerEntered += (s, e) => UpdateControls();
            PointerExited += (s, e) => UpdateControls();

            ContextMenu = CreateContextMenu();
        }

        private static Border RoundButton(Symbol symbol, double size, Thickness margin, HorizontalAlignment horizontal, string tip)
        {
            var button = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                HorizontalAlignment = horizontal,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = margin,
                Cursor = new Cursor(StandardCursorType.Hand),
                IsVisible = false,
                Child = new SymbolIcon { Symbol = symbol, FontSize = size, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            ToolTip.SetTip(button, tip);
            return button;
        }

        private Border CreateCornerHandle(int signX, int signY, HorizontalAlignment horizontal, VerticalAlignment vertical, StandardCursorType cursor)
        {
            var handle = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.White,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                // Centered on the corner so it's easy to grab
                Margin = new Thickness(signX < 0 ? -6 : 0, signY < 0 ? -6 : 0, signX > 0 ? -6 : 0, signY > 0 ? -6 : 0),
                Cursor = new Cursor(cursor),
                IsVisible = false
            };
            ToolTip.SetTip(handle, "Resize");
            handle.Bind(Border.BackgroundProperty, handle.GetResourceObservable("AccentFillColorDefaultBrush"));
            handle.PointerPressed += (s, e) =>
            {
                _cornerSignX = signX;
                _cornerSignY = signY;
                BeginDrag(DragMode.Resize, e);
            };
            return handle;
        }

        private Control CreatePlaceholder()
        {
            // The image data hasn't arrived from the sync service yet; the next sync fills it in
            var placeholder = new Border { CornerRadius = new CornerRadius(4) };
            placeholder.Bind(Border.BackgroundProperty, placeholder.GetResourceObservable("ControlFillColorSecondaryBrush"));
            var label = new TextBlock
            {
                Text = "Image syncing…",
                FontSize = 12,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("TextFillColorPrimaryBrush"));
            placeholder.Child = label;
            return placeholder;
        }

        private ContextMenu CreateContextMenu()
        {
            var menu = new ContextMenu();

            var copy = new MenuItem { Header = "Copy image", IsEnabled = _bitmap != null };
            copy.Click += async (s, e) =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await NoteImageBitmaps.CopyAsync(clipboard, Model.Id);
            };
            menu.Items.Add(copy);

            var rotateRight = new MenuItem { Header = "Rotate right 90°" };
            rotateRight.Click += (s, e) => RotateBy(90);
            menu.Items.Add(rotateRight);

            var rotateLeft = new MenuItem { Header = "Rotate left 90°" };
            rotateLeft.Click += (s, e) => RotateBy(-90);
            menu.Items.Add(rotateLeft);

            menu.Items.Add(new Separator());

            var remove = new MenuItem { Header = "Remove image" };
            remove.Click += (s, e) => _delete(this);
            menu.Items.Add(remove);

            return menu;
        }

        private void RotateBy(double degrees)
        {
            _rotation.Angle = NormalizeAngle(_rotation.Angle + degrees);
            SaveToModel();
        }

        private static double NormalizeAngle(double degrees)
        {
            degrees %= 360;
            if (degrees < 0) degrees += 360;
            return Math.Round(degrees, 1);
        }

        private void UpdateControls()
        {
            bool visible = IsPointerOver || _mode != DragMode.None;
            _outline.IsVisible = visible;
            _deleteButton.IsVisible = visible;
            _rotateHandle.IsVisible = visible;
            foreach (var handle in _cornerHandles) handle.IsVisible = visible;
        }

        private Point Center => new Point(Canvas.GetLeft(this) + Width / 2, Canvas.GetTop(this) + Height / 2);

        // The image's own x/y axes on the canvas, turned by its rotation
        private (Vector AxisX, Vector AxisY) Axes()
        {
            double radians = _rotation.Angle * Math.PI / 180;
            return (new Vector(Math.Cos(radians), Math.Sin(radians)), new Vector(-Math.Sin(radians), Math.Cos(radians)));
        }

        private void BeginDrag(DragMode mode, PointerPressedEventArgs e)
        {
            // Handled so the note window doesn't start moving itself
            e.Handled = true;
            if (Parent is not Canvas canvas || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            _mode = mode;
            _dragged = false;
            _dragStart = e.GetPosition(canvas);
            _startX = Canvas.GetLeft(this);
            _startY = Canvas.GetTop(this);
            _startWidth = Width;
            _startHeight = Height;
            _startAngle = _rotation.Angle;

            var center = Center;
            _startMouseAngle = Math.Atan2(_dragStart.Y - center.Y, _dragStart.X - center.X) * 180 / Math.PI;

            if (mode == DragMode.Resize)
            {
                var (axisX, axisY) = Axes();
                _pinnedCorner = center - axisX * (_cornerSignX * Width / 2) - axisY * (_cornerSignY * Height / 2);
            }

            e.Pointer.Capture(this);
            UpdateControls();
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_mode == DragMode.None || Parent is not Canvas canvas) return;

            var position = e.GetPosition(canvas);
            double dx = position.X - _dragStart.X;
            double dy = position.Y - _dragStart.Y;
            if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1 && !_dragged) return;
            _dragged = true;

            switch (_mode)
            {
                case DragMode.Move:
                    // Keep part of the image inside the note so it can always be grabbed again
                    double x = Math.Clamp(_startX + dx, -Width + 30, Math.Max(0, canvas.Bounds.Width - 30));
                    double y = Math.Clamp(_startY + dy, -Height + 30, Math.Max(0, canvas.Bounds.Height - 30));
                    Canvas.SetLeft(this, x);
                    Canvas.SetTop(this, y);
                    break;

                case DragMode.Resize:
                    Resize(position);
                    break;

                case DragMode.Rotate:
                    var center = Center;
                    double mouseAngle = Math.Atan2(position.Y - center.Y, position.X - center.X) * 180 / Math.PI;
                    double angle = _startAngle + (mouseAngle - _startMouseAngle);
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) angle = Math.Round(angle / SnapDegrees) * SnapDegrees;
                    _rotation.Angle = NormalizeAngle(angle);
                    break;
            }
        }

        private void Resize(Point mouse)
        {
            var (axisX, axisY) = Axes();

            // Project the mouse onto the diagonal from the pinned corner to the dragged one: the
            // image scales by how far along that diagonal the mouse is, keeping its aspect ratio
            Vector diagonal = axisX * (_cornerSignX * _startWidth) + axisY * (_cornerSignY * _startHeight);
            Vector toMouse = mouse - _pinnedCorner;
            double scale = (toMouse.X * diagonal.X + toMouse.Y * diagonal.Y) / (diagonal.X * diagonal.X + diagonal.Y * diagonal.Y);

            double width = Math.Max(MinImageWidth, _startWidth * scale);
            double height = width * _startHeight / _startWidth;

            // Grow away from the pinned corner, then convert the new center back to an unrotated top-left
            Point center = _pinnedCorner + axisX * (_cornerSignX * width / 2) + axisY * (_cornerSignY * height / 2);
            Width = width;
            Height = height;
            Canvas.SetLeft(this, center.X - width / 2);
            Canvas.SetTop(this, center.Y - height / 2);
        }

        private void EndDrag()
        {
            if (_mode == DragMode.None) return;
            _mode = DragMode.None;
            UpdateControls();

            if (_dragged) SaveToModel();
        }

        private void SaveToModel()
        {
            Model.X = Math.Round(Canvas.GetLeft(this), 1);
            Model.Y = Math.Round(Canvas.GetTop(this), 1);
            Model.Width = Math.Round(Width, 1);
            Model.Height = Math.Round(Height, 1);
            Model.Rotation = _rotation.Angle;
            _changed();
        }
    }
}
