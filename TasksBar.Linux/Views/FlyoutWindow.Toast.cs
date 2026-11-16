using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TasksBar.Linux.Effects;

namespace TasksBar.Linux.Views
{
    // The celebration toast at the bottom of the flyout (port of ShowCustomToast in the Windows app)
    public partial class FlyoutWindow
    {
        private int _toastCounter, _comboCount;
        private DateTime _lastCompletionToast = DateTime.MinValue;
        private static readonly Random _toastRandom = new();
        private static readonly string[] _cheers =
        {
            "Task completed", "Nice work!", "Done!", "Crushed it!", "One less thing!", "Great job!", "Nailed it!", "Checked off!"
        };

        private readonly ScaleTransform _toastScale = new(0.85, 0.85);
        private readonly TranslateTransform _toastMove = new(0, 24);
        private readonly ScaleTransform _toastIconScale = new(0, 0);
        private readonly ScaleTransform _toastRingScale = new(1, 1);
        private readonly ScaleTransform _toastComboScale = new(1, 1);
        private readonly ScaleTransform _toastProgressScale = new(1, 1);
        private readonly TranslateTransform _toastHeaderShift = new();
        private readonly TranslateTransform _toastTitleShift = new();
        private bool _toastReady;

        private void PrepareToast()
        {
            if (_toastReady) return;
            _toastReady = true;
            var group = new TransformGroup();
            group.Children.Add(_toastScale);
            group.Children.Add(_toastMove);
            ToastPopup.RenderTransform = group;
            ToastIconCircle.RenderTransform = _toastIconScale;
            ToastIconRing.RenderTransform = _toastRingScale;
            ToastCombo.RenderTransform = _toastComboScale;
            ToastProgress.RenderTransform = _toastProgressScale;
            ToastHeader.RenderTransform = _toastHeaderShift;
            ToastTaskTitle.RenderTransform = _toastTitleShift;
        }

        /// <summary>
        /// Completions (<paramref name="celebrate"/>) get the full treatment: springy pop, check badge drawing
        /// in, a shine sweep, confetti and a combo counter. Other messages use a calmer version.
        /// </summary>
        public async void ShowToast(string title, string? header = null, bool celebrate = true)
        {
            PrepareToast();
            int currentToast = ++_toastCounter;
            var accent = AccentColor();

            if (celebrate)
            {
                var now = DateTime.Now;
                _comboCount = (now - _lastCompletionToast).TotalSeconds < 10 ? _comboCount + 1 : 1;
                _lastCompletionToast = now;
            }
            bool combo = celebrate && _comboCount >= 2;

            ToastHeader.Text = header ?? (_comboCount switch
            {
                2 => "Double!",
                3 => "Triple!",
                >= 4 => "On a roll!",
                _ => _cheers[_toastRandom.Next(_cheers.Length)]
            });
            ToastTaskTitle.Text = title;
            ToastCombo.IsVisible = combo;
            ToastComboText.Text = "×" + _comboCount;

            // Accent tint and outline so the card belongs to the (translucent) window
            ToastTint.Background = Gradient(accent.WithAlpha(0x50), accent.WithAlpha(0x0C));
            ToastOutline.BorderBrush = Gradient(accent.WithAlpha(0xC8), accent.WithAlpha(0x30));

            bool wasVisible = ToastPopup.Opacity > 0.5;
            if (!wasVisible)
            {
                ToastPopup.Opacity(1, 160, from: 0);
                _toastMove.Y(0, 480, Ease.BackOut(0.55), from: 28);
                _toastScale.Scale(1, 650, Ease.ElasticOut(2, 6), from: 0.72);
            }
            else
            {
                // Already showing: a little bump instead of a new entrance
                ToastPopup.Opacity(1, 120);
                _toastMove.Y(0, 200);
                _toastScale.Scale(1.09, 90, Ease.QuadOut, done: () => _toastScale.Scale(1, 430, Ease.ElasticOut(2, 5)));
            }

            // Glow flare behind the card
            ToastGlow.Opacity(celebrate ? (combo ? 0.75 : 0.55) : 0.25, 180, Ease.QuadOut, from: 0,
                done: () => ToastGlow.Opacity(celebrate ? 0.12 : 0, 920, Ease.CubicInOut));

            // Shine sweep
            var shine = new LinearGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.Parse("#00FFFFFF"), 0.3),
                    new GradientStop(Color.Parse("#40FFFFFF"), 0.5),
                    new GradientStop(Color.Parse("#00FFFFFF"), 0.7)
                }
            };
            ToastShine.Background = shine;
            Tween.To(shine, "Shine", () => -1.2, x =>
            {
                shine.StartPoint = new RelativePoint(x, 0, RelativeUnit.Relative);
                shine.EndPoint = new RelativePoint(1 + x, 0.4, RelativeUnit.Relative);
            }, 1.2, 750, Ease.CubicInOut, 170, -1.2);

            // Check badge: circle springs in, check draws, ring ripples out
            _toastIconScale.Scale(1, 550, Ease.ElasticOut(1, 4), delay: 70, from: 0);
            ToastCheck.DashOffset(0, 260, Ease.CubicOut, delay: 240, from: 8);
            _toastRingScale.Scale(2.1, 650, Ease.CubicOut, delay: 220, from: 1);
            ToastIconRing.Opacity(0, 650, delay: 220, from: 0.9);

            // Text slides in, staggered
            SlideInText(ToastHeader, _toastHeaderShift, 110);
            SlideInText(ToastTaskTitle, _toastTitleShift, 180);

            if (combo) _toastComboScale.Scale(1, 600, Ease.ElasticOut(2, 4), delay: 280, from: 0);

            // Time-left bar
            const int holdMs = 3200;
            Tween.To(_toastProgressScale, "Progress", () => _toastProgressScale.ScaleX, v => _toastProgressScale.ScaleX = v,
                     0, holdMs, delayMs: 250, from: 1);

            // Confetti out of the badge, more for bigger combos
            int waited = 0;
            if (celebrate)
            {
                await Task.Delay(230);
                waited = 230;
                if (currentToast != _toastCounter) return;
                var center = ToastIcon.TranslatePoint(new Point(15, 15), EffectsLayer) ?? new Point(0, 0);
                CompletionEffects.PlayConfetti(EffectsLayer, center, accent, 20 + Math.Min(_comboCount - 1, 4) * 7);
            }

            await Task.Delay(holdMs + 250 - waited);

            // Only hide if nothing else was shown in the meantime
            if (currentToast == _toastCounter)
            {
                _toastMove.Y(14, 230, Ease.CubicIn);
                _toastScale.Scale(0.9, 230, Ease.CubicIn);
                ToastPopup.Opacity(0, 230);
                ToastGlow.Opacity(0, 200);
            }
        }

        private static LinearGradientBrush Gradient(Color from, Color to) => new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) }
        };

        private static void SlideInText(Control text, TranslateTransform shift, double delay)
        {
            shift.X(0, 320, Ease.CubicOut, delay, from: 12);
            text.Opacity(1, 260, delay: delay, from: 0);
        }
    }
}
