using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TasksBar
{
    /// <summary>
    /// Drag-to-reorder for the flyout's task list, phone style: the task lifts off as a floating card,
    /// the other tasks slide out of the way, a dashed outline shows where it will land, and on release it
    /// settles into place. Start a drag by pressing on a task's empty space and moving, or by pressing
    /// and holding on its text. Esc cancels.
    /// </summary>
    public class TaskReorder
    {
        private const double ItemSpacing = 6;          // ListViewItem bottom margin
        private const double LiftScale = 1.04;
        private const double EdgeScrollZone = 36;
        private static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(380);

        private readonly ListView _list;
        private readonly Canvas _layer;
        private readonly ObservableCollection<TaskItem> _items;
        private readonly Func<TaskItem, bool> _canDrag;
        private readonly Action<TaskItem> _reordered;
        private readonly DispatcherTimer _holdTimer;

        // Press that may turn into a drag
        private FrameworkElement? _pressContainer;
        private TaskItem? _pressItem;
        private Point _pressPoint;
        private bool _pressInText;          // pressed inside text that's being edited
        private TextBox? _pressTextBox;     // text under the press, if any
        private Point _pressTextPoint;

        // Active drag
        private bool _dragging;
        private int _fromIndex, _targetIndex;
        private List<(FrameworkElement Container, double Top, double Height)> _slots = new();
        private FrameworkElement? _dragContainer;
        private Border? _ghost;
        private ScaleTransform? _ghostScale;
        private RotateTransform? _ghostTilt;
        private DropShadowEffect? _ghostShadow;
        private Rectangle? _placeholder;
        private TranslateTransform? _placeholderScroll;
        private double _grabOffsetY, _ghostLeft, _startScroll, _placeholderTop;
        private Point _lastPoint;
        private ScrollViewer? _scroll;

        public bool IsDragging => _dragging;

        /// <summary>Raised after every drag, once the task has landed (moved or not).</summary>
        public event Action? DragEnded;

        public TaskReorder(ListView list, Canvas layer, ObservableCollection<TaskItem> items,
                           Func<TaskItem, bool> canDrag, Action<TaskItem> reordered)
        {
            _list = list;
            _layer = layer;
            _items = items;
            _canDrag = canDrag;
            _reordered = reordered;

            _holdTimer = new DispatcherTimer { Interval = HoldDelay };
            _holdTimer.Tick += (s, e) =>
            {
                _holdTimer.Stop();
                if (_pressContainer != null && Mouse.LeftButton == MouseButtonState.Pressed) StartDrag();
            };

            _list.PreviewMouseLeftButtonDown += OnMouseDown;
            _list.PreviewMouseMove += OnMouseMove;
            _list.PreviewMouseLeftButtonUp += OnMouseUp;
            _list.LostMouseCapture += (s, e) => { if (_dragging && Mouse.Captured != _list) Drop(cancel: false); };
            _list.PreviewKeyDown += (s, e) =>
            {
                if (_dragging && e.Key == Key.Escape) { Drop(cancel: true); e.Handled = true; }
            };
        }

        // ------------------------------------------------------------------ input

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_dragging || e.OriginalSource is not DependencyObject source) return;

            // The check circle and the ⋯ button keep their own clicks
            if (FindAncestor<ButtonBase>(source) != null) return;

            var container = FindAncestor<ListViewItem>(source);
            if (container?.DataContext is not TaskItem item || !_canDrag(item) || _items.Count < 2) return;

            _pressContainer = container;
            _pressItem = item;
            _pressPoint = e.GetPosition(_list);
            _pressTextBox = FindAncestor<TextBox>(source);

            // A task being edited behaves like text (title and details alike): dragging selects,
            // only holding still picks the task up
            _pressInText = _pressTextBox != null && container.IsKeyboardFocusWithin;
            if (_pressInText)
            {
                _holdTimer.Start();
                return;
            }

            // Anywhere else (including text that isn't being edited) the press is held back:
            // moving turns it into a drag, releasing in place becomes a normal click (see OnMouseUp)
            if (_pressTextBox != null)
            {
                _pressTextPoint = e.GetPosition(_pressTextBox);
                e.Handled = true;
                Mouse.Capture(_list, CaptureMode.SubTree);
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var point = e.GetPosition(_list);
            if (_dragging)
            {
                UpdateDrag(point);
                return;
            }
            if (_pressContainer == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { CancelPress(); return; }

            var delta = point - _pressPoint;
            bool moved = Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance ||
                         Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance;
            if (!moved) return;

            if (_pressInText) CancelPress();   // Selecting text, not dragging
            else StartDrag();
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragging)
            {
                Drop(cancel: false);
                e.Handled = true;
            }
            else if (_pressContainer != null && _pressTextBox != null && !_pressInText)
            {
                // It was a click on the text after all: start editing with the caret where it was clicked
                var textBox = _pressTextBox;
                var point = _pressTextPoint;
                CancelPress();
                e.Handled = true;
                textBox.Focus();
                int index = textBox.GetCharacterIndexFromPoint(point, snapToText: true);
                if (index >= 0)
                {
                    // snapToText gives the nearest character; clicking its right half means "after it"
                    var bounds = textBox.GetRectFromCharacterIndex(index);
                    if (point.X > bounds.Left + 0.5 * (textBox.GetRectFromCharacterIndex(index, trailingEdge: true).Left - bounds.Left)) index++;
                    textBox.CaretIndex = Math.Min(index, textBox.Text.Length);
                }
                else textBox.CaretIndex = textBox.Text.Length;
                return;
            }
            CancelPress();
        }

        private void CancelPress()
        {
            _holdTimer.Stop();
            if (!_dragging && _pressContainer != null && Mouse.Captured == _list) Mouse.Capture(null);
            _pressContainer = null;
            _pressItem = null;
            _pressTextBox = null;
        }

        // ------------------------------------------------------------------ drag lifecycle

        private void StartDrag()
        {
            if (_pressContainer == null || _pressItem == null) return;
            var container = _pressContainer;
            var item = _pressItem;
            _holdTimer.Stop();

            // Commit any edit in progress and stop text selection. Unfocusing collapses the
            // "Add details" line, so let the layout settle before measuring and snapshotting.
            Keyboard.ClearFocus();
            _list.UpdateLayout();

            _fromIndex = _items.IndexOf(item);
            if (_fromIndex < 0) { CancelPress(); return; }
            _targetIndex = _fromIndex;
            _dragContainer = container;
            _scroll ??= FindDescendant<ScrollViewer>(_list);
            _startScroll = _scroll?.VerticalOffset ?? 0;

            // Where every task sits right now (list coordinates, without any slide offsets)
            _slots = new List<(FrameworkElement, double, double)>();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) { CancelPress(); return; }
                if (c.RenderTransform is not TranslateTransform)
                    c.RenderTransform = new TranslateTransform();
                ((TranslateTransform)c.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
                ((TranslateTransform)c.RenderTransform).Y = 0;
                _slots.Add((c, c.TranslatePoint(new Point(0, 0), _list).Y, c.ActualHeight));
            }

            var origin = container.TranslatePoint(new Point(0, 0), _layer);
            _grabOffsetY = _pressPoint.Y - _slots[_fromIndex].Top;
            _ghostLeft = origin.X;
            _lastPoint = _pressPoint;

            CreatePlaceholder(container);
            CreateGhost(container, origin);

            // The real row stays in the list as the gap, invisible
            container.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(90)));

            _dragging = true;
            Mouse.Capture(_list, CaptureMode.SubTree);
            CompositionTarget.Rendering += OnFrame;
            _list.Focus();
        }

        private void UpdateDrag(Point point)
        {
            _lastPoint = point;
            if (_ghost == null) return;

            // The card follows the pointer vertically, a little beyond the list's edges at most
            var listInLayer = _list.TranslatePoint(new Point(0, 0), _layer);
            double top = point.Y - _grabOffsetY;
            double height = _slots[_fromIndex].Height;
            top = Math.Max(-height * 0.5, Math.Min(_list.ActualHeight - height * 0.5, top));
            Canvas.SetTop(_ghost, listInLayer.Y + top);

            // Which slot the card's centre is over, in content coordinates (accounts for scrolling)
            double scrolled = (_scroll?.VerticalOffset ?? 0) - _startScroll;
            double center = top + height / 2 + scrolled;
            int target = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (i == _fromIndex) continue;
                if (_slots[i].Top + _slots[i].Height / 2 < center) target++;
            }

            if (target != _targetIndex)
            {
                _targetIndex = target;
                ShiftOthers();
                MovePlaceholder();
            }
        }

        // Auto-scroll near the edges, and keep the outline glued to the content while scrolling
        private void OnFrame(object? sender, EventArgs e)
        {
            if (!_dragging || _scroll == null) return;

            double speed = 0;
            if (_lastPoint.Y < EdgeScrollZone) speed = -(EdgeScrollZone - _lastPoint.Y) / EdgeScrollZone * 9;
            else if (_lastPoint.Y > _list.ActualHeight - EdgeScrollZone)
                speed = (_lastPoint.Y - (_list.ActualHeight - EdgeScrollZone)) / EdgeScrollZone * 9;

            if (speed != 0)
            {
                double before = _scroll.VerticalOffset;
                _scroll.ScrollToVerticalOffset(Math.Max(0, Math.Min(_scroll.ScrollableHeight, before + speed)));
                if (_scroll.VerticalOffset != before) UpdateDrag(_lastPoint);
            }

            if (_placeholderScroll != null) _placeholderScroll.Y = -((_scroll.VerticalOffset) - _startScroll);
        }

        private void Drop(bool cancel)
        {
            if (!_dragging) return;
            _dragging = false;
            CompositionTarget.Rendering -= OnFrame;
            if (Mouse.Captured == _list) Mouse.Capture(null);

            if (cancel && _targetIndex != _fromIndex)
            {
                _targetIndex = _fromIndex;
                ShiftOthers();
                MovePlaceholder();
            }

            var ghost = _ghost;
            var placeholder = _placeholder;
            var container = _dragContainer;
            int from = _fromIndex, to = _targetIndex;
            var item = from < _items.Count ? _items[from] : null;

            // Fly the card into its slot and put it down
            var listInLayer = _list.TranslatePoint(new Point(0, 0), _layer);
            double scrolled = (_scroll?.VerticalOffset ?? 0) - _startScroll;
            double landTop = listInLayer.Y + SlotTop(to) - scrolled;

            var settle = TimeSpan.FromMilliseconds(260);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (ghost != null)
            {
                var land = new DoubleAnimation(landTop, settle) { EasingFunction = ease };
                land.Completed += (s, e) => Finish();
                ghost.BeginAnimation(Canvas.TopProperty, land);
                _ghostScale?.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, settle) { EasingFunction = ease });
                _ghostScale?.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, settle) { EasingFunction = ease });
                _ghostTilt?.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, settle) { EasingFunction = ease });
                _ghostShadow?.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, settle));
                _ghostShadow?.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(0, settle));
            }
            else Finish();

            placeholder?.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));

            void Finish()
            {
                // Swap the floating card for the real row in one go, so nothing jumps
                foreach (var slot in _slots)
                {
                    if (slot.Container.RenderTransform is TranslateTransform t)
                    {
                        t.BeginAnimation(TranslateTransform.YProperty, null);
                        t.Y = 0;
                    }
                }
                if (container != null)
                {
                    container.BeginAnimation(UIElement.OpacityProperty, null);
                    container.Opacity = 1;
                }
                if (ghost != null) _layer.Children.Remove(ghost);
                if (placeholder != null) _layer.Children.Remove(placeholder);
                _ghost = null;
                _placeholder = null;

                if (to != from && item != null)
                {
                    _items.Move(from, to);
                    _reordered(item);
                }

                if (!cancel && item != null) PlayLanding(item);
                _slots.Clear();

                // Pressing a row selects it in the ListView, which leaves a highlight behind; and rows
                // slid under a captured pointer keep a stale hover highlight until it's re-checked
                _list.UnselectAll();
                Mouse.Synchronize();
                DragEnded?.Invoke();
            }
        }

        // ------------------------------------------------------------------ visuals

        private void CreateGhost(FrameworkElement container, Point origin)
        {
            double width = container.ActualWidth, height = container.ActualHeight;
            var accent = AccentColor();

            // Snapshot of the row (through a VisualBrush, so its position in the list doesn't offset the image)
            var dpi = VisualTreeHelper.GetDpi(container);
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY)),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
                dc.DrawRectangle(new VisualBrush(container) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                                 null, new Rect(0, 0, width, height));
            bitmap.Render(drawing);

            _ghostScale = new ScaleTransform(1, 1);
            _ghostTilt = new RotateTransform(0);
            var transforms = new TransformGroup();
            transforms.Children.Add(_ghostScale);
            transforms.Children.Add(_ghostTilt);

            _ghostShadow = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Direction = 270, Opacity = 0, Color = Colors.Black };

            var baseBrush = (TryFind("SolidBackgroundFillColorBaseBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x20, 0x20, 0x20);
            var card = new Grid();
            card.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(baseBrush), Opacity = 0.94 });
            card.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush(TaskCompletionEffects.WithAlpha(accent, 0x38), TaskCompletionEffects.WithAlpha(accent, 0x08), 0)
            });
            card.Children.Add(new Image { Source = bitmap, Width = width, Height = height, Stretch = Stretch.None });

            _ghost = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(TaskCompletionEffects.WithAlpha(accent, 0x90)),
                Child = card,
                Effect = _ghostShadow,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = transforms,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_ghost, origin.X);
            Canvas.SetTop(_ghost, origin.Y);
            _layer.Children.Add(_ghost);

            // Lift: springs up a little, tilts, and casts a shadow
            var lift = TimeSpan.FromMilliseconds(320);
            var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
            _ghostScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, LiftScale, lift) { EasingFunction = spring });
            _ghostScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, LiftScale, lift) { EasingFunction = spring });
            _ghostTilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, -1.2, lift) { EasingFunction = spring });
            _ghostShadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, 0.45, lift));
            _ghostShadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(0, 6, lift));
        }

        private void CreatePlaceholder(FrameworkElement container)
        {
            var accent = AccentColor();
            _placeholderScroll = new TranslateTransform();
            _placeholder = new Rectangle
            {
                Width = container.ActualWidth,
                Height = container.ActualHeight,
                RadiusX = 8,
                RadiusY = 8,
                Stroke = new SolidColorBrush(TaskCompletionEffects.WithAlpha(accent, 0xB0)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(TaskCompletionEffects.WithAlpha(accent, 0x1C)),
                RenderTransform = _placeholderScroll,
                IsHitTestVisible = false,
                Opacity = 0
            };
            var origin = container.TranslatePoint(new Point(0, 0), _layer);
            Canvas.SetLeft(_placeholder, origin.X);
            _placeholderTop = origin.Y;
            Canvas.SetTop(_placeholder, _placeholderTop);
            _layer.Children.Add(_placeholder);
            _placeholder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));

            // Marching dashes, so the landing spot feels alive
            _placeholder.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(0, -7, TimeSpan.FromMilliseconds(600)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        // The other tasks slide up or down to open a gap at the target slot
        private void ShiftOthers()
        {
            double gap = _slots[_fromIndex].Height + ItemSpacing;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            for (int i = 0; i < _slots.Count; i++)
            {
                if (i == _fromIndex) continue;
                double offset = 0;
                if (_fromIndex < _targetIndex && i > _fromIndex && i <= _targetIndex) offset = -gap;
                else if (_fromIndex > _targetIndex && i >= _targetIndex && i < _fromIndex) offset = gap;

                if (_slots[i].Container.RenderTransform is TranslateTransform t && Math.Abs(t.Y - offset) > 0.1)
                    t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            }
        }

        private void MovePlaceholder()
        {
            if (_placeholder == null) return;
            var listInLayer = _list.TranslatePoint(new Point(0, 0), _layer);
            double top = listInLayer.Y + SlotTop(_targetIndex);
            _placeholder.BeginAnimation(Canvas.TopProperty,
                new DoubleAnimation(top, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>Top of the dragged task if it were dropped at <paramref name="target"/> (list coordinates, at drag start's scroll).</summary>
        private double SlotTop(int target)
        {
            double top = _slots[0].Top;
            int placed = 0;
            for (int i = 0; i < _slots.Count && placed < target; i++)
            {
                if (i == _fromIndex) continue;
                top += _slots[i].Height + ItemSpacing;
                placed++;
            }
            return top;
        }

        // A soft accent flash and a tiny bounce where the task landed
        private void PlayLanding(TaskItem item)
        {
            _list.UpdateLayout();
            if (_list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement row) return;

            var accent = AccentColor();
            var position = row.TranslatePoint(new Point(0, 0), _layer);
            var flash = new Border
            {
                Width = row.ActualWidth,
                Height = row.ActualHeight,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(TaskCompletionEffects.WithAlpha(accent, 0x40)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(flash, position.X);
            Canvas.SetTop(flash, position.Y);
            _layer.Children.Add(flash);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(550)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (s, e) => _layer.Children.Remove(flash);
            flash.BeginAnimation(UIElement.OpacityProperty, fade);

            var bounce = new ScaleTransform(1, 1, row.ActualWidth / 2, row.ActualHeight / 2);
            var previous = row.RenderTransform;
            var group = new TransformGroup();
            group.Children.Add(bounce);
            if (previous is TranslateTransform translate) group.Children.Add(translate);
            row.RenderTransform = group;
            var settle = new DoubleAnimationUsingKeyFrames();
            settle.KeyFrames.Add(new EasingDoubleKeyFrame(1.025, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            settle.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)),
                new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 }));
            settle.Completed += (s, e) => row.RenderTransform = previous is TranslateTransform ? previous : new TranslateTransform();
            bounce.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
            bounce.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
        }

        // ------------------------------------------------------------------ helpers

        private Color AccentColor() =>
            (TryFind("AccentFillColorDefaultBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x00, 0x78, 0xD4);

        private object? TryFind(string key) => _list.TryFindResource(key);

        private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
        {
            for (var current = from; current != null; current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                     ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            {
                if (current is T match) return match;
            }
            return null;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var deeper = FindDescendant<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
