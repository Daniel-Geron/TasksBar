using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TasksBar.Services
{
    /// <summary>
    /// System-wide keyboard shortcuts for sticky notes. They are registered with Windows on a
    /// hidden message-only window, so they work while TasksBar is hidden or another app has focus.
    /// Shortcuts are stored in the config as text like "Ctrl+Alt+N".
    /// </summary>
    public static class HotkeyManager
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        private const int NewNoteId = 1;
        private const int OpenAllNotesId = 2;

        private static HwndSource? _source;
        private static readonly HashSet<int> _registered = new HashSet<int>();

        /// <summary>Shortcuts that Windows refused because another app already uses them.</summary>
        public static HashSet<string> FailedShortcuts { get; } = new HashSet<string>();

        public static void Initialize()
        {
            if (_source != null) return;

            // HWND_MESSAGE parent: an invisible window that only receives messages
            _source = new HwndSource(new HwndSourceParameters("TasksBarHotkeys") { ParentWindow = new IntPtr(-3) });
            _source.AddHook(WndProc);
            Apply();
        }

        /// <summary>(Re)registers both shortcuts from the current settings.</summary>
        public static void Apply()
        {
            if (_source == null) return;

            Suspend();
            FailedShortcuts.Clear();
            Register(NewNoteId, AppConfig.Settings.NewNoteHotkey);
            Register(OpenAllNotesId, AppConfig.Settings.OpenAllNotesHotkey);
        }

        /// <summary>Releases the shortcuts, e.g. while the user is recording a new one in settings.</summary>
        public static void Suspend()
        {
            if (_source == null) return;
            foreach (int id in _registered) UnregisterHotKey(_source.Handle, id);
            _registered.Clear();
        }

        private static void Register(int id, string shortcut)
        {
            if (!TryParse(shortcut, out var modifiers, out var key)) return;

            uint mods = MOD_NOREPEAT;
            if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
            if (modifiers.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
            if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
            if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= MOD_WIN;

            if (RegisterHotKey(_source!.Handle, id, mods, (uint)KeyInterop.VirtualKeyFromKey(key)))
                _registered.Add(id);
            else
                FailedShortcuts.Add(shortcut);
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY) return IntPtr.Zero;

            switch (wParam.ToInt32())
            {
                case NewNoteId:
                    OpenNewNote();
                    handled = true;
                    break;
                case OpenAllNotesId:
                    OpenAllNotes();
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }

        // ------------------------------------------------------------------ actions

        public static void OpenNewNote()
        {
            var mainWindow = Application.Current.Windows.OfType<TasksFlyoutWindow>().FirstOrDefault();
            var note = new StickyNoteWindow(mainWindow?.Left - 260 ?? 100, mainWindow?.Top ?? 100);
            note.Show();
            note.Activate();
            note.NoteTextBox.Focus();
        }

        public static void OpenAllNotes()
        {
            var openWindows = Application.Current.Windows.OfType<StickyNoteWindow>().ToList();

            foreach (var note in LocalDataManager.ActiveNotes.ToList())
            {
                var window = openWindows.FirstOrDefault(w => w.GetModel() == note);
                if (window == null)
                {
                    window = new StickyNoteWindow(note);
                    window.Show();
                    openWindows.Add(window);
                }
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            }

            // Bring every note in front of whatever app is focused
            foreach (var window in openWindows) window.Activate();
        }

        // ------------------------------------------------------------------ text format

        private static readonly Key[] ModifierOnlyKeys =
        {
            Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt,
            Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin, Key.System
        };

        public static bool IsModifierKey(Key key) => ModifierOnlyKeys.Contains(key);

        public static string Format(ModifierKeys modifiers, Key key)
        {
            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            if (key != Key.None) parts.Add(KeyName(key));
            return string.Join("+", parts);
        }

        private static string KeyName(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
            return key.ToString();
        }

        public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
        {
            modifiers = ModifierKeys.None;
            key = Key.None;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts.Take(parts.Length - 1))
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": modifiers |= ModifierKeys.Control; break;
                    case "alt": modifiers |= ModifierKeys.Alt; break;
                    case "shift": modifiers |= ModifierKeys.Shift; break;
                    case "win": modifiers |= ModifierKeys.Windows; break;
                    default: return false;
                }
            }

            string last = parts.Last();
            if (last.Length == 1 && char.IsDigit(last[0])) key = Key.D0 + (last[0] - '0');
            else if (!Enum.TryParse(last, true, out key)) return false;

            return modifiers != ModifierKeys.None && key != Key.None && !IsModifierKey(key);
        }
    }
}
