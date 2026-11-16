using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TasksBar.Linux.Effects;

namespace TasksBar.Linux.Views
{
    /// <summary>
    /// Drag-to-reorder for the task list, phone style (port of the Windows app's TaskReorder): the task
    /// lifts off as a floating card, the others slide out of the way, a dashed outline shows where it will
    /// land, and on release it settles into place. Press anywhere on a task and move to drag; a plain click
    /// on its text still edits. While a task is being edited its text behaves like text, and holding still
    /// picks it up. Esc cancels.
    /// </summary>
    public class TaskReorder
    {
        private const double ItemSpacing = 6;
        private const double LiftScale = 1.04;
        private const double EdgeScrollZone = 36;
        private const double DragThreshold = 4;

        private readonly ItemsControl _list;
        private readonly ScrollViewer _scroll;
        private readonly Canvas _layer;
        private readonly ObservableCollection<TaskItem> _items;
        private readonly Func<TaskItem, bool> _canDrag;
        private readonly Action<TaskItem> _reordered;
        private readonly DispatcherTimer _holdTimer;
        private readonly DispatcherTimer _scrollTimer;

        // Press that may turn into a drag
        private Control? _pressContainer;
        private TaskItem? _pressItem;
        private Point _pressPoint;
        private bool _pressInText;           // pressed inside a task that's being edited
        private TextBox? _pressTextBox;      // text under the press, if any
        private Point _pressTextPoint;
        private IPointer? _pointer;

        // Active drag
        private bool _dragging;
        private int _fromIndex, _targetIndex;
        private List<(Control Container, double Top, double Height)> _slots = new();
        private Border? _ghost;
        private ScaleTransform? _ghostScale;
        private RotateTransform? _ghostTilt;
        private Rectangle? _placeholder;
        private TranslateTransform? _placeholderScroll;
        private double _grabOffsetY, _startScroll;
        private Point _lastPoint;

        public bool IsDragging => _dragging;
        public event Action? DragEnded;

        public TaskReorder(ItemsControl list, ScrollViewer scroll, Canvas layer, ObservableCollection<TaskItem> items,
                           Func<TaskItem, bool> canDrag, Action<TaskItem> reordered)
        {
            _list = list;
            _scroll = scroll;
            _layer = layer;
            _items = items;
            _canDrag = canDrag;
            _reordered = reordered;

            _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
            _holdTimer.Tick += (s, e) =>
            {
                _holdTimer.Stop();
                if (_pressContainer != null) StartDrag();
            };
            _scrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (s, e) => OnFrame());

            _list.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
            _list.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
            _list.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
            _list.AddHandler(InputElement.PointerCaptureLostEvent, (s, e) => { if (_dragging) Drop(cancel: false); });
            _list.AddHandler(InputElement.KeyDownEvent, (s, e) =>
            {
                if (_dragging && e.Key == Key.Escape) { Drop(cancel: true); e.Handled = true; }
            }, RoutingStrategies.Tunnel);
        }

        // ------------------------------------------------------------------ input

        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_dragging || e.Source is not Visual source) return;
            if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;

            // The check circle and the ⋯ button keep their own clicks
            if (source.FindAncestorOfType<CheckCircle>(includeSelf: true) != null) return;
            if (source.FindAncestorOfType<Button>(includeSelf: true) != null) return;

            var container = ContainerOf(source);
            if (container?.DataContext is not TaskItem item || !_canDrag(item) || _items.Count < 2) return;

            _pressContainer = container;
            _pressItem = item;
            _pressPoint = e.GetPosition(_list);
            _pressTextBox = source.FindAncestorOfType<TextBox>(includeSelf: true);
            _pointer = e.Pointer;

            // A task being edited behaves like text: dragging selects, only holding still picks it up
            _pressInText = _pressTextBox != null && container.IsKeyboardFocusWithin;
            if (_pressInText)
            {
                _holdTimer.Start();
                return;
            }

            // Anywhere else (including text not being edited) the press is held back: moving makes it
            // a drag, releasing in place becomes a normal click on the text
            if (_pressTextBox != null)
            {
                _pressTextPoint = e.GetPosition(_pressTextBox);
                e.Handled = true;
                e.Pointer.Capture(_list);
            }
        }

        private void OnMoved(object? sender, PointerEventArgs e)
        {
            var point = e.GetPosition(_list);
            if (_dragging)
            {
                UpdateDrag(point);
                e.Handled = true;
                return;
            }
            if (_pressContainer == null) return;

            var delta = point - _pressPoint;
            if (Math.Abs(delta.X) <= DragThreshold && Math.Abs(delta.Y) <= DragThreshold) return;

            if (_pressInText) CancelPress();   // Selecting text, not dragging
            else StartDrag();
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_dragging)
            {
                Drop(cancel: false);
                e.Handled = true;
            }
            else if (_pressContainer != null && _pressTextBox != null && !_pressInText)
            {
                // It was a click on the text after all: start editing with the caret where it was clicked
                var box = _pressTextBox;
                var point = _pressTextPoint;
                CancelPress();
                e.Handled = true;
                box.Focus();
                var presenter = box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
                int length = box.Text?.Length ?? 0;
                int caret = length;
                if (presenter?.TextLayout is { } layout)
                {
                    var inPresenter = box.TranslatePoint(point, presenter) ?? point;
                    caret = layout.HitTestPoint(inPresenter).TextPosition;
                }
                box.CaretIndex = Math.Clamp(caret, 0, length);
                box.SelectionStart = box.SelectionEnd = box.CaretIndex;
                return;
            }
            CancelPress();
        }

        private void CancelPress()
        {
            _holdTimer.Stop();
            if (!_dragging && _pointer?.Captured == _list) _pointer.Capture(null);
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

            // Commit any edit in progress; unfocusing collapses the "Add details" line, so settle the layout first
            TopLevel.GetTopLevel(_list)?.FocusManager?.ClearFocus();
            _list.UpdateLayout();

            _fromIndex = _items.IndexOf(item);
            if (_fromIndex < 0) { CancelPress(); return; }
            _targetIndex = _fromIndex;
            _startScroll = _scroll.Offset.Y;

            _slots = new List<(Control, double, double)>();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_list.ContainerFromIndex(i) is not Control c) { CancelPress(); return; }
                if (c.RenderTransform is not TranslateTransform) c.RenderTransform = new TranslateTransform();
                var t = (TranslateTransform)c.RenderTransform;
                Tween.Stop(t, "Y");
                t.Y = 0;
                _slots.Add((c, c.TranslatePoint(new Point(0, 0), _list)?.Y ?? 0, c.Bounds.Height));
            }

            var origin = container.TranslatePoint(new Point(0, 0), _layer) ?? new Point(0, 0);
            _grabOffsetY = _pressPoint.Y - _slots[_fromIndex].Top;
            _lastPoint = _pressPoint;

            CreatePlaceholder(container, origin);
            CreateGhost(container, origin);
            container.Opacity(0, 90);

            _dragging = true;
            _pointer?.Capture(_list);
            _scrollTimer.Start();
        }

        private void UpdateDrag(Point point)
        {
            _lastPoint = point;
            if (_ghost == null) return;

            // The card follows the pointer vertically, a little beyond the list's edges at most
            var listInLayer = _list.TranslatePoint(new Point(0, 0), _layer) ?? new Point(0, 0);
            double height = _slots[_fromIndex].Height;
            double viewTop = point.Y - _grabOffsetY - (_scroll.Offset.Y - _startScroll);
            double viewportTop = _scroll.TranslatePoint(new Point(0, 0), _layer)?.Y ?? 0;
            double layerTop = listInLayer.Y + point.Y - _grabOffsetY;
            layerTop = Math.Max(viewportTop - height * 0.5, Math.Min(viewportTop + _scroll.Bounds.Height - height * 0.5, layerTop));
            Canvas.SetTop(_ghost, layerTop);

            // Which slot the card's centre is over (list coordinates already include scrolling)
            double center = layerTop - listInLayer.Y + height / 2;
            int target = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (i == _fromIndex) continue;
                if (_slots[i].Top + _slots[i].Height / 2 < center) target++;
            }
            _ = viewTop;

            if (target != _targetIndex)
            {
                _targetIndex = target;
                ShiftOthers();
                MovePlaceholder();
            }
        }

        // Auto-scroll near the edges, and keep the outline glued to the content while scrolling
        private void OnFrame()
        {
            if (!_dragging) { _scrollTimer.Stop(); return; }

            var inViewport = _list.TranslatePoint(_lastPoint, _scroll) ?? _lastPoint;
            double speed = 0;
            if (inViewport.Y < EdgeScrollZone) speed = -(EdgeScrollZone - inViewport.Y) / EdgeScrollZone * 9;
            else if (inViewport.Y > _scroll.Bounds.Height - EdgeScrollZone)
                speed = (inViewport.Y - (_scroll.Bounds.Height - EdgeScrollZone)) / EdgeScrollZone * 9;

            if (speed != 0)
            {
                double before = _scroll.Offset.Y;
                double max = Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);
                double next = Math.Max(0, Math.Min(max, before + speed));
                if (next != before)
                {
                    _scroll.Offset = new Vector(_scroll.Offset.X, next);
                    // The list moved under a still pointer: the pointer is now at a different list position
                    _lastPoint = new Point(_lastPoint.X, _lastPoint.Y + (next - before));
                    UpdateDrag(_lastPoint);
                }
            }

            if (_placeholderScroll != null) _placeholderScroll.Y = -(_scroll.Offset.Y - _startScroll);
        }

        private void Drop(bool cancel)
        {
            if (!_dragging) return;
            _dragging = false;
            _scrollTimer.Stop();
            if (_pointer?.Captured == _list) _pointer.Capture(null);

            if (cancel && _targetIndex != _fromIndex)
            {
                _targetIndex = _fromIndex;
                ShiftOthers();
                MovePlaceholder();
            }

            var ghost = _ghost;
            var placeholder = _placeholder;
            var container = _pressContainer;
            int from = _fromIndex, to = _targetIndex;
            var item = from < _items.Count ? _items[from] : null;
            var slots = _slots;

            // Fly the card into its slot and put it down
            var listInLayer = _list.TranslatePoint(new Point(0, 0), _layer) ?? new Point(0, 0);
            double scrolled = _scroll.Offset.Y - _startScroll;
            double landTop = listInLayer.Y + SlotTop(to) - scrolled + scrolled; // list coordinates scroll with the list

            if (ghost != null)
            {
                ghost.Top(listInLayer.Y + SlotTop(to), 260, Ease.CubicOut, done: Finish);
                _ghostScale?.Scale(1, 260, Ease.CubicOut);
                _ghostTilt?.Angle(0, 260, Ease.CubicOut);
                Tween.To(ghost, "Shadow", () => 1, v => ghost.BoxShadow = Shadow(v), 0, 260, from: 1);
            }
            else Finish();
            _ = landTop;

            if (placeholder != null) placeholder.Opacity(0, 200);

            void Finish()
            {
                // Swap the floating card for the real row in one go, so nothing jumps
                foreach (var slot in slots)
                {
                    if (slot.Container.RenderTransform is TranslateTransform t)
                    {
                        Tween.Stop(t, "Y");
                        t.Y = 0;
                    }
                }
                if (container != null)
                {
                    Tween.Stop(container, "Opacity");
                    container.Opacity = 1;
                }
                if (ghost != null) _layer.Children.Remove(ghost);
                if (placeholder != null)
                {
                    Tween.Stop(placeholder, "Dash");
                    _layer.Children.Remove(placeholder);
                }
                _ghost = null;
                _placeholder = null;

                if (to != from && item != null)
                {
                    _items.Move(from, to);
                    _reordered(item);
                }

                if (!cancel && item != null) Dispatcher.UIThread.Post(() => PlayLanding(item), DispatcherPriority.Loaded);
                _slots = new();
                _pressContainer = null;
                _pressItem = null;
                DragEnded?.Invoke();
            }
        }

        // ------------------------------------------------------------------ visuals

        private static BoxShadows Shadow(double strength) =>
            new(new BoxShadow { OffsetY = 6 * strength, Blur = 22 * strength, Color = Color.FromArgb((byte)(115 * strength), 0, 0, 0) });

        private void CreateGhost(Control container, Point origin)
        {
            double width = container.Bounds.Width, height = container.Bounds.Height;
            var accent = Accent();

            // Snapshot of the row
            double scaling = TopLevel.GetTopLevel(_list)?.RenderScaling ?? 1;
            var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)Math.Ceiling(width * scaling)), Math.Max(1, (int)Math.Ceiling(height * scaling))),
                                                new Vector(96 * scaling, 96 * scaling));
            bitmap.Render(container);

            _ghostScale = new ScaleTransform(1, 1);
            _ghostTilt = new RotateTransform(0);
            var transforms = new TransformGroup();
            transforms.Children.Add(_ghostScale);
            transforms.Children.Add(_ghostTilt);

            var baseColor = (_list.FindResource(Application.Current!.ActualThemeVariant, "SolidBackgroundFillColorBaseBrush") as ISolidColorBrush)?.Color
                            ?? Color.Parse("#202020");
            var card = new Panel();
            card.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(baseColor), Opacity = 0.94 });
            card.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(accent.WithAlpha(0x38), 0), new GradientStop(accent.WithAlpha(0x08), 1) }
                }
            });
            card.Children.Add(new Image { Source = bitmap, Width = width, Height = height, Stretch = Stretch.None });

            _ghost = new Border
            {
                Width = width, Height = height,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(accent.WithAlpha(0x90)),
                Child = card,
                RenderTransform = transforms,
                IsHitTestVisible = false,
                BoxShadow = Shadow(0)
            };
            Canvas.SetLeft(_ghost, origin.X);
            Canvas.SetTop(_ghost, origin.Y);
            _layer.Children.Add(_ghost);

            // Lift: springs up a little, tilts, and casts a shadow
            var ghost = _ghost;
            _ghostScale.Scale(LiftScale, 320, Ease.BackOut(0.6), from: 1);
            _ghostTilt.Angle(-1.2, 320, Ease.BackOut(0.6), from: 0);
            Tween.To(ghost, "Shadow", () => 0, v => ghost.BoxShadow = Shadow(v), 1, 320, from: 0);
        }

        private void CreatePlaceholder(Control container, Point origin)
        {
            var accent = Accent();
            _placeholderScroll = new TranslateTransform();
            _placeholder = new Rectangle
            {
                Width = container.Bounds.Width, Height = container.Bounds.Height,
                RadiusX = 8, RadiusY = 8,
                Stroke = new SolidColorBrush(accent.WithAlpha(0xB0)), StrokeThickness = 1.5,
                StrokeDashArray = new AvaloniaList<double> { 4, 3 },
                Fill = new SolidColorBrush(accent.WithAlpha(0x1C)),
                RenderTransform = _placeholderScroll,
                IsHitTestVisible = false, Opacity = 0
            };
            Canvas.SetLeft(_placeholder, origin.X);
            Canvas.SetTop(_placeholder, origin.Y);
            _layer.Children.Add(_placeholder);
            _placeholder.Opacity(1, 200);

            // Marching dashes, so the landing spot feels alive
            var placeholder = _placeholder;
            Tween.To(placeholder, "Dash", () => 0, v => placeholder.StrokeDashOffset = v, -7, 600, from: 0, repeatForever: true);
        }

        // The other tasks slide up or down to open a gap at the target slot
        private void ShiftOthers()
        {
            double gap = _slots[_fromIndex].Height + ItemSpacing;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (i == _fromIndex) continue;
                double offset = 0;
                if (_fromIndex < _targetIndex && i > _fromIndex && i <= _targetIndex) offset = -gap;
                else if (_fromIndex > _targetIndex && i >= _targetIndex && i < _fromIndex) offset = gap;

                if (_slots[i].Container.RenderTransform is TranslateTransform t && Math.Abs(t.Y - offset) > 0.1)
                    t.Y(offset, 220, Ease.CubicOut);
            }
        }

        private void MovePlaceholder()
        {
            if (_placeholder == null) return;
            var listInLayer = _list.TranslatePoint(new Point(0, 0), _layer) ?? new Point(0, 0);
            double scrolled = _scroll.Offset.Y - _startScroll;
            _placeholder.Top(listInLayer.Y + scrolled + SlotTop(_targetIndex), 220, Ease.CubicOut);
        }

        /// <summary>Top of the dragged task if it were dropped at <paramref name="target"/> (list coordinates).</summary>
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
            if (_list.ContainerFromItem(item) is not Control row) return;
            var accent = Accent();
            var position = row.TranslatePoint(new Point(0, 0), _layer) ?? new Point(0, 0);
            var flash = new Border
            {
                Width = row.Bounds.Width, Height = row.Bounds.Height,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(accent.WithAlpha(0x40)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(flash, position.X);
            Canvas.SetTop(flash, position.Y);
            _layer.Children.Add(flash);
            flash.Opacity(0, 550, Ease.QuadOut, from: 1, done: () => _layer.Children.Remove(flash));

            var bounce = new ScaleTransform(1, 1);
            var previous = row.RenderTransform;
            row.RenderTransform = bounce;
            bounce.Scale(1.025, 90, done: () => bounce.Scale(1, 330, Ease.ElasticOut(1, 5), done: () => row.RenderTransform = previous));
        }

        // ------------------------------------------------------------------ helpers

        private Color Accent() =>
            (_list.FindResource(Application.Current!.ActualThemeVariant, "AccentFillColorDefaultBrush") as ISolidColorBrush)?.Color
            ?? Color.Parse("#4CC2FF");

        private Control? ContainerOf(Visual source)
        {
            for (Visual? v = source; v != null; v = v.GetVisualParent())
            {
                if (v is Control c && c.GetVisualParent() is Panel panel && panel.GetVisualParent() is ItemsPresenter) return c;
            }
            return null;
        }
    }
}
