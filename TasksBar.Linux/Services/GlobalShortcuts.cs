using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Input;
using Avalonia.Threading;

namespace TasksBar.Linux.Services
{
    /// <summary>
    /// The system-wide sticky note shortcuts from Settings (stored like the Windows app's, e.g. "Ctrl+Alt+N").
    /// Linux has no single way to register one, so this picks what the desktop supports:
    /// <list type="bullet">
    /// <item>GNOME (Ubuntu's desktop, Wayland or X11): added to GNOME's own custom keyboard shortcuts,
    /// which run <c>tasksbar --new-note</c> / <c>--open-notes</c>. They show up in Settings → Keyboard.</item>
    /// <item>Other X11 desktops (KDE on X11, Xfce, …): grabbed directly from the X server.</item>
    /// <item>Elsewhere (e.g. KDE on Wayland): the user binds those commands in the desktop's shortcut settings.</item>
    /// </list>
    /// </summary>
    public static class GlobalShortcuts
    {
        public enum Method { Gnome, X11, Manual }

        public const string NewNoteId = "tasksbar-new-note";
        public const string OpenNotesId = "tasksbar-open-notes";

        public static Method Current { get; private set; } = Method.Manual;

        /// <summary>Shortcuts the desktop refused because another app already uses them.</summary>
        public static HashSet<string> FailedShortcuts { get; } = new();

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
            bool wayland = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" ||
                           !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

            if (desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase) && Gnome.IsAvailable()) Current = Method.Gnome;
            else if (!wayland && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && X11.Start()) Current = Method.X11;
            else Current = Method.Manual;

            Apply();
        }

        /// <summary>(Re)registers both shortcuts from the current settings.</summary>
        public static void Apply()
        {
            FailedShortcuts.Clear();
            Register(NewNoteId, "New sticky note", InstanceChannel.NewNote, AppConfig.Settings.NewNoteHotkey);
            Register(OpenNotesId, "Open all sticky notes", InstanceChannel.OpenNotes, AppConfig.Settings.OpenAllNotesHotkey);
        }

        /// <summary>Releases the shortcuts while one is being recorded, so pressing it records instead of firing.</summary>
        public static void Suspend()
        {
            Register(NewNoteId, "New sticky note", InstanceChannel.NewNote, "");
            Register(OpenNotesId, "Open all sticky notes", InstanceChannel.OpenNotes, "");
        }

        private static void Register(string id, string name, string command, string shortcut)
        {
            switch (Current)
            {
                case Method.Gnome:
                    if (!Gnome.Set(id, name, command, shortcut) && shortcut.Length > 0) FailedShortcuts.Add(shortcut);
                    break;
                case Method.X11:
                    if (!X11.Set(id, shortcut, command) && shortcut.Length > 0) FailedShortcuts.Add(shortcut);
                    break;
            }
        }

        /// <summary>The command a desktop shortcut should run for this action.</summary>
        public static string CommandFor(string action) => $"{Autostart.ProgramPath} {action}";

        public static void RunAction(string command)
        {
            var app = Avalonia.Application.Current as App;
            if (command == InstanceChannel.NewNote) Views.StickyNoteWindow.OpenNew(app?.Flyout);
            else if (command == InstanceChannel.OpenNotes) Views.StickyNoteWindow.OpenAll();
        }

        // ------------------------------------------------------------------ text format

        [Flags]
        public enum Modifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Super = 8 }

        public static string Format(Modifiers modifiers, string? key)
        {
            var parts = new List<string>();
            if (modifiers.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
            if (modifiers.HasFlag(Modifiers.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(Modifiers.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(Modifiers.Super)) parts.Add("Super");
            if (!string.IsNullOrEmpty(key)) parts.Add(key);
            return string.Join("+", parts);
        }

        public static bool TryParse(string? text, out Modifiers modifiers, out string key)
        {
            modifiers = Modifiers.None;
            key = "";
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts.Take(parts.Length - 1))
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": modifiers |= Modifiers.Ctrl; break;
                    case "alt": modifiers |= Modifiers.Alt; break;
                    case "shift": modifiers |= Modifiers.Shift; break;
                    case "super": case "win": case "meta": modifiers |= Modifiers.Super; break;
                    default: return false;
                }
            }
            key = parts.Last();
            return modifiers != Modifiers.None && Keysym(key) != null;
        }

        public static Modifiers FromAvalonia(KeyModifiers modifiers)
        {
            var result = Modifiers.None;
            if (modifiers.HasFlag(KeyModifiers.Control)) result |= Modifiers.Ctrl;
            if (modifiers.HasFlag(KeyModifiers.Alt)) result |= Modifiers.Alt;
            if (modifiers.HasFlag(KeyModifiers.Shift)) result |= Modifiers.Shift;
            if (modifiers.HasFlag(KeyModifiers.Meta)) result |= Modifiers.Super;
            return result;
        }

        public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

        /// <summary>The name a key is stored under, or null for keys shortcuts can't use.</summary>
        public static string? KeyName(Key key)
        {
            if (key >= Key.A && key <= Key.Z) return ((char)('A' + (key - Key.A))).ToString();
            if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return ((char)('0' + (key - Key.NumPad0))).ToString();
            if (key >= Key.F1 && key <= Key.F24) return "F" + (1 + (key - Key.F1));
            return key switch
            {
                Key.Space => "Space",
                Key.Left => "Left",
                Key.Right => "Right",
                Key.Up => "Up",
                Key.Down => "Down",
                Key.Home => "Home",
                Key.End => "End",
                Key.PageUp => "PageUp",
                Key.PageDown => "PageDown",
                Key.Insert => "Insert",
                Key.Delete => "Delete",
                _ => null
            };
        }

        /// <summary>The X11/GNOME name of a stored key ("N" → "n", "PageUp" → "Prior").</summary>
        public static string? Keysym(string key)
        {
            if (key.Length == 1 && char.IsLetter(key[0])) return key.ToLowerInvariant();
            if (key.Length == 1 && char.IsDigit(key[0])) return key;
            if (Regex.IsMatch(key, "^F([1-9]|1[0-9]|2[0-4])$")) return key;
            return key switch
            {
                "Space" => "space",
                "Left" or "Right" or "Up" or "Down" or "Home" or "End" or "Insert" or "Delete" => key,
                "PageUp" or "Prior" => "Prior",
                "PageDown" or "Next" => "Next",
                _ => null
            };
        }

        // ------------------------------------------------------------------ GNOME

        private static class Gnome
        {
            private const string Schema = "org.gnome.settings-daemon.plugins.media-keys";
            private const string BasePath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/";

            public static bool IsAvailable() => Run(out _, "list-keys", Schema) && Run(out _, "list-keys", Schema + ".custom-keybinding:" + BasePath + "x/");

            public static bool Set(string id, string name, string command, string shortcut)
            {
                string path = BasePath + id + "/";
                string entry = Schema + ".custom-keybinding:" + path;
                var list = ReadList();

                if (!TryParse(shortcut, out var modifiers, out var key))
                {
                    // Removed: take it out of GNOME's list too
                    if (list.Remove(path)) WriteList(list);
                    Run(out _, "reset", entry, "binding");
                    Run(out _, "reset", entry, "command");
                    Run(out _, "reset", entry, "name");
                    return true;
                }

                string binding = (modifiers.HasFlag(Modifiers.Ctrl) ? "<Control>" : "") +
                                 (modifiers.HasFlag(Modifiers.Alt) ? "<Alt>" : "") +
                                 (modifiers.HasFlag(Modifiers.Shift) ? "<Shift>" : "") +
                                 (modifiers.HasFlag(Modifiers.Super) ? "<Super>" : "") + Keysym(key);

                bool ok = Run(out _, "set", entry, "name", "TasksBar: " + name) &&
                          Run(out _, "set", entry, "command", $"'{Autostart.ProgramPath.Replace("'", "'\\''")}' {command}") &&
                          Run(out _, "set", entry, "binding", binding);
                if (ok && !list.Contains(path))
                {
                    list.Add(path);
                    ok = WriteList(list);
                }
                return ok;
            }

            private static List<string> ReadList()
            {
                if (!Run(out string output, "get", Schema, "custom-keybindings")) return new List<string>();
                return Regex.Matches(output, "'([^']*)'").Select(m => m.Groups[1].Value).ToList();
            }

            private static bool WriteList(List<string> list) =>
                Run(out _, "set", Schema, "custom-keybindings", "[" + string.Join(", ", list.Select(p => $"'{p}'")) + "]");

            private static bool Run(out string output, params string[] args)
            {
                output = "";
                try
                {
                    var info = new ProcessStartInfo("gsettings") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                    foreach (var arg in args) info.ArgumentList.Add(arg);
                    using var process = Process.Start(info);
                    if (process == null) return false;
                    output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    return process.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        // ------------------------------------------------------------------ X11

        /// <summary>
        /// Grabs keys on the X root window from its own X connection and thread, so they work while
        /// another app has focus. NumLock and CapsLock are grabbed in every combination, so they don't matter.
        /// </summary>
        private static class X11
        {
            private const string Lib = "libX11.so.6";
            [DllImport(Lib)] private static extern IntPtr XOpenDisplay(IntPtr name);
            [DllImport(Lib)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
            [DllImport(Lib)] private static extern IntPtr XStringToKeysym(string name);
            [DllImport(Lib)] private static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);
            [DllImport(Lib)] private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window, bool ownerEvents, int pointerMode, int keyboardMode);
            [DllImport(Lib)] private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window);
            [DllImport(Lib)] private static extern int XPending(IntPtr display);
            [DllImport(Lib)] private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);
            [DllImport(Lib)] private static extern int XSync(IntPtr display, bool discard);
            [DllImport(Lib)] private static extern IntPtr XSetErrorHandler(IntPtr handler);

            private delegate int ErrorHandler(IntPtr display, IntPtr errorEvent);

            private const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 0x10, Mod4Mask = 0x40;
            private const int KeyPress = 2, GrabModeAsync = 1;
            private static readonly uint[] LockVariants = { 0, LockMask, Mod2Mask, LockMask | Mod2Mask };

            private static IntPtr _display, _root;
            private static readonly ConcurrentQueue<Action> _work = new();
            private static readonly Dictionary<string, (int Keycode, uint Mods, string Command)> _grabs = new();
            private static readonly ErrorHandler _handler = OnError;
            private static IntPtr _previousHandler;
            private static volatile bool _grabFailed;

            public static bool Start()
            {
                try
                {
                    _display = XOpenDisplay(IntPtr.Zero);
                    if (_display == IntPtr.Zero) return false;
                    _root = XDefaultRootWindow(_display);

                    // A grab another app holds fails with BadAccess. Xlib's default handler would end the
                    // process, so errors on this connection are caught; others go on to Avalonia's handler.
                    _previousHandler = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(_handler));

                    new Thread(Loop) { IsBackground = true, Name = "TasksBar shortcuts" }.Start();
                    return true;
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                {
                    return false;
                }
            }

            private static int OnError(IntPtr display, IntPtr errorEvent)
            {
                if (display == _display)
                {
                    _grabFailed = true;
                    return 0;
                }
                if (_previousHandler == IntPtr.Zero) return 0;
                try
                {
                    // Avalonia's own handler comes back as its delegate type, so call it without casting
                    var previous = Marshal.GetDelegateForFunctionPointer(_previousHandler, typeof(ErrorHandler));
                    return previous.DynamicInvoke(display, errorEvent) is int result ? result : 0;
                }
                catch
                {
                    return 0; // Never let an exception escape into Xlib
                }
            }

            /// <summary>Grabs (or with an empty shortcut, releases) the key for this action. Waits for the X server's answer.</summary>
            public static bool Set(string id, string shortcut, string command)
            {
                bool result = true;
                using var done = new ManualResetEventSlim();
                _work.Enqueue(() =>
                {
                    try { result = SetOnThread(id, shortcut, command); }
                    finally { done.Set(); }
                });
                done.Wait(2000);
                return result;
            }

            private static bool SetOnThread(string id, string shortcut, string command)
            {
                if (_grabs.Remove(id, out var old))
                {
                    foreach (var variant in LockVariants) XUngrabKey(_display, old.Keycode, old.Mods | variant, _root);
                }

                if (!TryParse(shortcut, out var modifiers, out var key)) { XSync(_display, false); return true; }
                var keysym = XStringToKeysym(Keysym(key)!);
                int keycode = XKeysymToKeycode(_display, keysym);
                if (keycode == 0) return false;

                uint mods = (modifiers.HasFlag(Modifiers.Shift) ? ShiftMask : 0) |
                            (modifiers.HasFlag(Modifiers.Ctrl) ? ControlMask : 0) |
                            (modifiers.HasFlag(Modifiers.Alt) ? Mod1Mask : 0) |
                            (modifiers.HasFlag(Modifiers.Super) ? Mod4Mask : 0);

                _grabFailed = false;
                foreach (var variant in LockVariants) XGrabKey(_display, keycode, mods | variant, _root, false, GrabModeAsync, GrabModeAsync);
                XSync(_display, false);

                if (_grabFailed)
                {
                    foreach (var variant in LockVariants) XUngrabKey(_display, keycode, mods | variant, _root);
                    XSync(_display, false);
                    return false;
                }
                _grabs[id] = (keycode, mods, command);
                return true;
            }

            private static void Loop()
            {
                IntPtr ev = Marshal.AllocHGlobal(256);
                while (true)
                {
                    while (_work.TryDequeue(out var job)) job();

                    if (XPending(_display) == 0)
                    {
                        Thread.Sleep(25);
                        continue;
                    }

                    XNextEvent(_display, ev);
                    if (Marshal.ReadInt32(ev, 0) != KeyPress) continue;

                    // XKeyEvent on 64-bit: state at offset 80, keycode at 84
                    uint state = (uint)Marshal.ReadInt32(ev, 80) & (ShiftMask | ControlMask | Mod1Mask | Mod4Mask);
                    int keycode = Marshal.ReadInt32(ev, 84);
                    foreach (var grab in _grabs.Values)
                    {
                        if (grab.Keycode == keycode && grab.Mods == state)
                        {
                            string command = grab.Command;
                            Dispatcher.UIThread.Post(() => RunAction(command));
                        }
                    }
                }
            }
        }
    }
}
