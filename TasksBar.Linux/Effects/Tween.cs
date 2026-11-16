using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Threading;

namespace TasksBar.Linux.Effects
{
    /// <summary>
    /// The easing curves of the Windows app (WPF's formulas), so animations move exactly the same way.
    /// Each takes t in 0..1 and returns the eased progress.
    /// </summary>
    public static class Ease
    {
        public static Func<double, double> Linear => t => t;

        public static Func<double, double> QuadOut => Out(t => t * t);
        public static Func<double, double> QuadIn => t => t * t;
        public static Func<double, double> CubicOut => Out(t => t * t * t);
        public static Func<double, double> CubicIn => t => t * t * t;
        public static Func<double, double> CubicInOut => InOut(t => t * t * t);
        public static Func<double, double> QuinticOut => Out(t => t * t * t * t * t);
        public static Func<double, double> SineInOut => InOut(t => 1 - Math.Sin(Math.PI / 2 * (1 - t)));

        /// <summary>WPF BackEase (EaseOut): overshoots by <paramref name="amplitude"/> before settling.</summary>
        public static Func<double, double> BackOut(double amplitude = 1) =>
            Out(t => t * t * t - t * amplitude * Math.Sin(Math.PI * t));

        /// <summary>WPF ElasticEase (EaseOut): springs past the target and wobbles in.</summary>
        public static Func<double, double> ElasticOut(int oscillations = 3, double springiness = 3) =>
            Out(t =>
            {
                double expo = springiness == 0 ? t : (Math.Exp(springiness * t) - 1) / (Math.Exp(springiness) - 1);
                return expo * Math.Sin((2 * Math.PI * oscillations + Math.PI / 2) * t);
            });

        private static Func<double, double> Out(Func<double, double> easeIn) => t => 1 - easeIn(1 - t);

        private static Func<double, double> InOut(Func<double, double> easeIn) =>
            t => t < 0.5 ? easeIn(t * 2) * 0.5 : (1 - easeIn((1 - t) * 2)) * 0.5 + 0.5;
    }

    /// <summary>
    /// A tiny animation engine driven by the UI thread at ~60 fps. Starting a tween on a target/key that
    /// is already animating replaces it from its current value (like WPF's SnapshotAndReplace), so the
    /// storyboards of the Windows app translate one to one.
    /// </summary>
    public static class Tween
    {
        private class Running
        {
            public object Target = null!;
            public string Key = "";
            public Func<double> Get = null!;
            public Action<double> Set = null!;
            public double? From;
            public double To;
            public double Start;
            public double Duration;
            public double Delay;
            public Func<double, double> Easing = Ease.Linear;
            public Action? Completed;
            public bool Repeat, AutoReverse;
            public bool Begun;
        }

        private static readonly List<Running> _running = new();
        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static DispatcherTimer? _timer;

        private static double Now => _clock.Elapsed.TotalMilliseconds;

        /// <summary>
        /// Animates a value from its current value (or <paramref name="from"/>) to <paramref name="to"/>.
        /// <paramref name="key"/> names the animated property, so a later tween of the same property replaces this one.
        /// </summary>
        public static void To(object target, string key, Func<double> get, Action<double> set, double to,
                              double durationMs, Func<double, double>? easing = null, double delayMs = 0,
                              double? from = null, Action? completed = null, bool repeatForever = false, bool autoReverse = false)
        {
            Stop(target, key);
            var tween = new Running
            {
                Target = target, Key = key, Get = get, Set = set, From = from, To = to,
                Start = Now, Duration = Math.Max(1, durationMs), Delay = delayMs,
                Easing = easing ?? Ease.Linear, Completed = completed,
                Repeat = repeatForever, AutoReverse = autoReverse
            };
            _running.Add(tween);
            if (delayMs <= 0) Begin(tween);
            EnsureTimer();
        }

        /// <summary>Stops an animation, leaving the value where it is.</summary>
        public static void Stop(object target, string key) =>
            _running.RemoveAll(r => ReferenceEquals(r.Target, target) && r.Key == key);

        public static bool IsRunning(object target, string key) =>
            _running.Exists(r => ReferenceEquals(r.Target, target) && r.Key == key);

        private static void Begin(Running tween)
        {
            tween.Begun = true;
            tween.From ??= tween.Get();
            tween.Set(tween.From.Value);
        }

        private static void EnsureTimer()
        {
            if (_timer != null) { if (!_timer.IsEnabled) _timer.Start(); return; }
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (s, e) => Tick());
            _timer.Start();
        }

        private static void Tick()
        {
            double now = Now;
            foreach (var tween in _running.ToArray())
            {
                if (!_running.Contains(tween)) continue;
                double elapsed = now - tween.Start - tween.Delay;
                if (elapsed < 0) continue;
                if (!tween.Begun) Begin(tween);

                double progress = elapsed / tween.Duration;
                if (tween.Repeat)
                {
                    double cycle = tween.AutoReverse ? progress % 2 : progress % 1;
                    double t = tween.AutoReverse && cycle > 1 ? 2 - cycle : Math.Min(cycle, 1);
                    tween.Set(tween.From!.Value + (tween.To - tween.From.Value) * tween.Easing(t));
                    continue;
                }

                if (progress >= 1)
                {
                    tween.Set(tween.To);
                    _running.Remove(tween);
                    tween.Completed?.Invoke();
                }
                else
                {
                    tween.Set(tween.From!.Value + (tween.To - tween.From.Value) * tween.Easing(progress));
                }
            }

            if (_running.Count == 0) _timer?.Stop();
        }
    }
}
