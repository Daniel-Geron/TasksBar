using System;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using TasksBar.Linux.Effects;

namespace TasksBar.Linux.Views
{
    /// <summary>
    /// The task's completion circle, ported from the Windows app's ToggleButton template with the same
    /// timings: hover grows it with a spring, turns the ring accent, blooms a breathing glow and draws a
    /// ghost checkmark; checking it pops the fill in and draws the real checkmark.
    /// </summary>
    public class CheckCircle : Panel
    {
        public static readonly StyledProperty<bool> IsCheckedProperty =
            AvaloniaProperty.Register<CheckCircle, bool>(nameof(IsChecked));

        public bool IsChecked
        {
            get => GetValue(IsCheckedProperty);
            set => SetValue(IsCheckedProperty, value);
        }

        /// <summary>Raised when the user clicks the circle (IsChecked already flipped).</summary>
        public event EventHandler? Toggled;

        private const string CheckGeometry = "M 5,9.2 L 7.8,12 L 13,6.4";

        private readonly ScaleTransform _controlScale = new(1, 1);
        private readonly ScaleTransform _glowScale = new(1, 1);
        private readonly ScaleTransform _fillScale = new(0, 0);
        private readonly Ellipse _glow, _outerRing, _hoverRing, _fill;
        private readonly Path _ghostCheck, _checkMark;
        private bool _hovering;

        public CheckCircle()
        {
            Width = 18;
            Height = 18;
            Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.Hand);
            RenderTransform = _controlScale;

            _glow = new Ellipse
            {
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransform = _glowScale,
                // Fades out toward the edge so it reads as a soft halo, not a flat disc
                OpacityMask = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#FF000000"), 0),
                        new GradientStop(Color.Parse("#CC000000"), 0.6),
                        new GradientStop(Color.Parse("#00000000"), 1)
                    }
                }
            };
            _glow.Bind(Shape.FillProperty, this.GetResourceObservable("AccentFillColorDefaultBrush"));

            _outerRing = new Ellipse { StrokeThickness = 1.5 };
            _hoverRing = new Ellipse { StrokeThickness = 2, Opacity = 0 };
            _hoverRing.Bind(Shape.StrokeProperty, this.GetResourceObservable("AccentFillColorDefaultBrush"));

            _ghostCheck = MakeCheck("AccentFillColorDefaultBrush");
            _fill = new Ellipse { Opacity = 0, RenderTransform = _fillScale };
            _fill.Bind(Shape.FillProperty, this.GetResourceObservable("AccentFillColorDefaultBrush"));
            _checkMark = MakeCheck("TextOnAccentFillColorPrimaryBrush");

            Children.Add(_glow);
            Children.Add(_outerRing);
            Children.Add(_hoverRing);
            Children.Add(_ghostCheck);
            Children.Add(_fill);
            Children.Add(_checkMark);

            UpdateRingBrush();
        }

        private Path MakeCheck(string brushKey)
        {
            // Dash length 7 (in stroke widths) covers the whole tick, so offset 7 hides it and 0 shows it.
            // Also invisible until drawn: the round cap would otherwise leave a dot at the tick's start.
            var path = new Path
            {
                Data = Geometry.Parse(CheckGeometry),
                StrokeThickness = 1.8,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                StrokeDashArray = new AvaloniaList<double> { 7, 7 },
                StrokeDashOffset = 7,
                Opacity = 0,
                IsHitTestVisible = false
            };
            path.Bind(Shape.StrokeProperty, this.GetResourceObservable(brushKey));
            return path;
        }

        private IDisposable? _ringBinding;

        private void UpdateRingBrush()
        {
            _ringBinding?.Dispose();
            _ringBinding = _outerRing.Bind(Shape.StrokeProperty,
                this.GetResourceObservable(IsChecked ? "AccentFillColorDefaultBrush" : "TextFillColorSecondaryBrush"));
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsCheckedProperty) PlayChecked(IsChecked);
        }

        // ------------------------------------------------------------------ hover

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            _hovering = true;

            _controlScale.Scale(1.2, 350, Ease.ElasticOut(1, 4));
            _hoverRing.Opacity(1, 150);

            // Glow blooms out, then keeps breathing gently while the pointer stays
            _glow.Opacity(0.8, 450, Ease.QuadOut);
            _glowScale.Scale(2.4, 450, Ease.QuinticOut, done: () =>
            {
                if (_hovering) _glowScale.Scale(2.65, 1400, Ease.SineInOut, from: 2.4, repeat: true, autoReverse: true);
            });

            // Preview checkmark draws in
            _ghostCheck.Opacity(0.85, 100, delay: 80);
            _ghostCheck.DashOffset(0, 300, Ease.CubicOut, delay: 80, from: 7);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hovering = false;

            _controlScale.Scale(1, 200, Ease.CubicOut);
            _hoverRing.Opacity(0, 200);
            _glow.Opacity(0, 300, Ease.QuadOut);
            _glowScale.Scale(1, 300, Ease.CubicOut);
            _ghostCheck.Opacity(0, 150);
            _ghostCheck.DashOffset(7, 150);
        }

        // ------------------------------------------------------------------ press

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Handled = true;

            _controlScale.Scale(0.85, 50);
            IsChecked = !IsChecked;
            Toggled?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _controlScale.Scale(_hovering ? 1.2 : 1, 100);
        }

        // ------------------------------------------------------------------ checked

        private void PlayChecked(bool isChecked)
        {
            UpdateRingBrush();
            if (isChecked)
            {
                // The circle fills with a pop, then the checkmark draws itself in
                _fill.Opacity = 1;
                _fillScale.Scale(1, 350, Ease.BackOut(0.8), from: 0);
                Tween.To(_checkMark, "Opacity", () => _checkMark.Opacity, v => _checkMark.Opacity = v, 1, 1, delayMs: 150);
                _checkMark.DashOffset(0, 250, Ease.CubicOut, delay: 150, from: 7);
            }
            else
            {
                _checkMark.DashOffset(7, 120);
                _checkMark.Opacity(0, 120);
                _fill.Opacity(0, 200);
                _fillScale.Scale(0, 200, Ease.CubicIn);
            }
        }

        /// <summary>Sets the state without animating (when a row is first shown).</summary>
        public void SetCheckedInstantly(bool isChecked)
        {
            SetCurrentValue(IsCheckedProperty, isChecked);
            Tween.Stop(_fillScale, "Scale");
            Tween.Stop(_checkMark, "Dash");
            Tween.Stop(_checkMark, "Opacity");
            Tween.Stop(_fill, "Opacity");
            _fill.Opacity = isChecked ? 1 : 0;
            _fillScale.ScaleX = _fillScale.ScaleY = isChecked ? 1 : 0;
            _checkMark.Opacity = isChecked ? 1 : 0;
            _checkMark.StrokeDashOffset = isChecked ? 0 : 7;
            UpdateRingBrush();
        }
    }
}
