using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TasksBar
{
    /// <summary>
    /// One pasted image floating on a sticky note. Drag it to move, drag a corner handle to resize
    /// (keeps the aspect ratio and pins the opposite corner), drag the round handle above it to rotate
    /// (Shift snaps to 15°), and use the ✕ or right-click menu to remove it.
    /// </summary>
    public class NoteImageView : Grid
    {
        private const double MinImageWidth = 40;
        private const double SnapDegrees = 15;

        public NoteImage Model { get; }

        private readonly Action _changed;
        private readonly Action<NoteImageView> _delete;
        private readonly BitmapSource? _bitmap;
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

        // Resize state: which corner is dragged (+1/-1 per axis) and the opposite corner that stays put
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
            Cursor = Cursors.SizeAll;

            // Rotation turns the image around its center; X/Y stay the unrotated top-left corner
            _rotation = new RotateTransform(model.Rotation);
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = _rotation;

            _bitmap = Services.NoteImageStore.Load(model.Id);
            Children.Add(_bitmap != null
                ? new Image { Source = _bitmap, Stretch = Stretch.Fill }
                : CreatePlaceholder());

            _outline = new Border { BorderThickness = new Thickness(1.5), IsHitTestVisible = false, Visibility = Visibility.Hidden };
            _outline.SetResourceReference(Border.BorderBrushProperty, "SystemAccentColorPrimaryBrush");
            Children.Add(_outline);

            _deleteButton = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(200, 32, 32, 32)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Remove image",
                Visibility = Visibility.Hidden,
                Child = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Dismiss12) { Foreground = Brushes.White, FontSize = 12 }
            };
            _deleteButton.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                _delete(this);
            };
            Children.Add(_deleteButton);

            _rotateHandle = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                // Inside the image: the note clips anything above it, and images often sit at the top
                Margin = new Thickness(0, 10, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Rotate (hold Shift to snap)",
                Visibility = Visibility.Hidden,
                Child = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ArrowRotateClockwise16) { Foreground = Brushes.White, FontSize = 14 }
            };
            _rotateHandle.SetResourceReference(Border.BackgroundProperty, "SystemAccentColorPrimaryBrush");
            _rotateHandle.MouseLeftButtonDown += (s, e) => BeginDrag(DragMode.Rotate, e);
            Children.Add(_rotateHandle);

            _cornerHandles = new[]
            {
                CreateCornerHandle(-1, -1, HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE),
                CreateCornerHandle(1, -1, HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW),
                CreateCornerHandle(-1, 1, HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW),
                CreateCornerHandle(1, 1, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE)
            };
            foreach (var handle in _cornerHandles) Children.Add(handle);

            MouseLeftButtonDown += (s, e) => { if (!e.Handled) BeginDrag(DragMode.Move, e); };
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += (s, e) => { if (_mode != DragMode.None) ReleaseMouseCapture(); };
            LostMouseCapture += (s, e) => EndDrag();
            MouseEnter += (s, e) => UpdateControls();
            MouseLeave += (s, e) => UpdateControls();

            ContextMenu = CreateContextMenu();
        }

        private Border CreateCornerHandle(int signX, int signY, HorizontalAlignment horizontal, VerticalAlignment vertical, Cursor cursor)
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
                Cursor = cursor,
                ToolTip = "Resize",
                Visibility = Visibility.Hidden
            };
            handle.SetResourceReference(Border.BackgroundProperty, "SystemAccentColorPrimaryBrush");
            handle.MouseLeftButtonDown += (s, e) =>
            {
                _cornerSignX = signX;
                _cornerSignY = signY;
                BeginDrag(DragMode.Resize, e);
            };
            return handle;
        }

        private FrameworkElement CreatePlaceholder()
        {
            // The image data hasn't arrived from Google yet; the next sync fills it in
            var placeholder = new Border { CornerRadius = new CornerRadius(4) };
            placeholder.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
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
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            placeholder.Child = label;
            return placeholder;
        }

        private ContextMenu CreateContextMenu()
        {
            var menu = new ContextMenu();

            var copy = new MenuItem { Header = "Copy image", IsEnabled = _bitmap != null };
            copy.Click += (s, e) =>
            {
                try { if (_bitmap != null) Clipboard.SetImage(_bitmap); } catch { }
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
            var visibility = IsMouseOver || _mode != DragMode.None ? Visibility.Visible : Visibility.Hidden;
            _outline.Visibility = visibility;
            _deleteButton.Visibility = visibility;
            _rotateHandle.Visibility = visibility;
            foreach (var handle in _cornerHandles) handle.Visibility = visibility;
        }

        private Point Center => new Point(Canvas.GetLeft(this) + Width / 2, Canvas.GetTop(this) + Height / 2);

        // The image's own x/y axes on the canvas, turned by its rotation
        private (Vector AxisX, Vector AxisY) Axes()
        {
            double radians = _rotation.Angle * Math.PI / 180;
            return (new Vector(Math.Cos(radians), Math.Sin(radians)), new Vector(-Math.Sin(radians), Math.Cos(radians)));
        }

        private void BeginDrag(DragMode mode, MouseButtonEventArgs e)
        {
            // Handled so the note window doesn't start moving itself
            e.Handled = true;
            if (Parent is not Canvas canvas) return;

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

            CaptureMouse();
            UpdateControls();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
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
                    double x = Math.Clamp(_startX + dx, -Width + 30, Math.Max(0, canvas.ActualWidth - 30));
                    double y = Math.Clamp(_startY + dy, -Height + 30, Math.Max(0, canvas.ActualHeight - 30));
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
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) angle = Math.Round(angle / SnapDegrees) * SnapDegrees;
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
            double scale = ((mouse - _pinnedCorner) * diagonal) / (diagonal * diagonal);

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
