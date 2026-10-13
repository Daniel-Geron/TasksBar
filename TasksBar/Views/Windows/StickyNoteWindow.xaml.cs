using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace TasksBar
{
    public partial class StickyNoteWindow : FluentWindow
    {

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData);

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr refData);

        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_NCACTIVATE = 0x0086;

        // Kept in a static field so the GC never collects the delegate native code is calling
        private static readonly SubclassProc _keepBackdropProc = KeepBackdropWhenInactive;
        private IntPtr _hwnd;

        private StickyNoteModel _model;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Windows 11 only draws Mica/Acrylic on the active window and shows a flat
            // fallback color on inactive ones. A native subclass is used because it sees
            // WM_NCACTIVATE before WPF's window chrome, which would otherwise pass the real state on.
            _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            SetWindowSubclass(_hwnd, _keepBackdropProc, UIntPtr.Zero, UIntPtr.Zero);

            Wpf.Ui.Appearance.ApplicationThemeManager.Changed += OnAppThemeChanged;
        }

        private void OnAppThemeChanged(Wpf.Ui.Appearance.ApplicationTheme theme, System.Windows.Media.Color accent)
        {
            // WPF UI makes the window background transparent with SetCurrentValue, which a
            // DynamicResource re-evaluation silently undoes. Every new note re-applies the theme,
            // so older notes got their opaque background back and lost Mica/Acrylic.
            // Re-apply once the resource refresh has finished.
            Dispatcher.BeginInvoke(new Action(ReapplyBackdrop), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ReapplyBackdrop()
        {
            var expected = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;
            this.WindowBackdropType = WindowBackdropType.None;
            this.WindowBackdropType = expected;
            UpdateInkColor();
        }

        private static IntPtr KeepBackdropWhenInactive(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
        {
            if (msg == WM_NCACTIVATE)
            {
                // Always tell Windows the frame is active so DWM keeps the backdrop on every note.
                // Real focus and WPF's Activated/Deactivated come from WM_ACTIVATE and are unaffected.
                // lParam -1 skips repainting the hidden native frame.
                return DefWindowProc(hWnd, msg, new IntPtr(1), new IntPtr(-1));
            }
            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }
        public StickyNoteModel GetModel() => _model;

        // Constructor for loading EXISTING notes
        public StickyNoteWindow(StickyNoteModel model)
        {
            InitializeComponent();
            _model = model;
            ApplyThemeSettings();

            this.Left = _model.Left;
            this.Top = _model.Top;
            _suppressSync = true;
            NoteTextBox.Text = _model.Text;
            _suppressSync = false;

            LoadInk();
            UpdateInkColor();
            // Auto-save whenever a line is drawn or erased
            NoteInkCanvas.Strokes.StrokesChanged += OnStrokesChanged;
            this.StateChanged += Window_StateChanged;
        }

        // Constructor for creating BRAND NEW notes
        public StickyNoteWindow(double startLeft, double startTop)
        {
            InitializeComponent();
            ApplyThemeSettings();

            _model = new StickyNoteModel { Left = startLeft, Top = startTop };
            this.Left = startLeft;
            this.Top = startTop;

            LocalDataManager.ActiveNotes.Add(_model);
            Services.StickyNoteSync.MarkDirty(_model);
            UpdateInkColor();
            NoteInkCanvas.Strokes.StrokesChanged += OnStrokesChanged;
            this.StateChanged += Window_StateChanged;
        }
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                }
                catch { }
            }
        }
        private void ApplyThemeSettings()
        {
            if (AppConfig.Settings.AppTheme == 1)
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            else if (AppConfig.Settings.AppTheme == 2)
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            else
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
                Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
            }
            Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();

            this.WindowBackdropType = AppConfig.Settings.UseAcrylic ? WindowBackdropType.Acrylic : WindowBackdropType.Mica;
        }

        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender == TextModeBtn)
            {
                TextModeBtn.IsChecked = true;
                PenModeBtn.IsChecked = false;
                EraserModeBtn.IsChecked = false;

                // Allow clicks to pass through the canvas so you can type
                NoteInkCanvas.IsHitTestVisible = false;
            }
            else if (sender == PenModeBtn)
            {
                TextModeBtn.IsChecked = false;
                PenModeBtn.IsChecked = true;
                EraserModeBtn.IsChecked = false;

                NoteInkCanvas.IsHitTestVisible = true;
                NoteInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }
            else if (sender == EraserModeBtn)
            {
                TextModeBtn.IsChecked = false;
                PenModeBtn.IsChecked = false;
                EraserModeBtn.IsChecked = true;

                NoteInkCanvas.IsHitTestVisible = true;
                NoteInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
            }
        }

        private bool _suppressSync;

        private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e) => SaveInk();

        private void SaveInk()
        {
            if (_model == null || _suppressSync) return;

            using (MemoryStream ms = new MemoryStream())
            {
                NoteInkCanvas.Strokes.Save(ms);
                _model.InkData = Convert.ToBase64String(ms.ToArray());
                Services.StickyNoteSync.MarkDirty(_model);
            }
        }

        /// <summary>Shows content that sync pulled from another device, without re-uploading it.</summary>
        public void ReloadFromModel()
        {
            _suppressSync = true;
            try
            {
                if (NoteTextBox.Text != _model.Text) NoteTextBox.Text = _model.Text;

                NoteInkCanvas.Strokes.StrokesChanged -= OnStrokesChanged;
                NoteInkCanvas.Strokes = new StrokeCollection();
                LoadInk();
                UpdateInkColor();
                NoteInkCanvas.Strokes.StrokesChanged += OnStrokesChanged;
            }
            finally
            {
                _suppressSync = false;
            }
        }

        private void LoadInk()
        {
            if (!string.IsNullOrEmpty(_model.InkData))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(_model.InkData);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        NoteInkCanvas.Strokes = new StrokeCollection(ms);
                    }
                }
                catch { } // Ignore if string is corrupted
            }
        }
        public void UpdateInkColor()
        {
            var currentTheme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            var targetColor = currentTheme == Wpf.Ui.Appearance.ApplicationTheme.Dark
                ? System.Windows.Media.Colors.White
                : System.Windows.Media.Colors.Black;

            // 1. Update the pen for future drawings
            NoteInkCanvas.DefaultDrawingAttributes.Color = targetColor;

            // 2. Loop through all existing drawings and forcefully change their color!
            foreach (var stroke in NoteInkCanvas.Strokes)
            {
                stroke.DrawingAttributes.Color = targetColor;
            }
        }
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
                _model.Left = this.Left;
                _model.Top = this.Top;
                LocalDataManager.SaveNotes();
            }
        }

        private void NoteTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_model != null && !_suppressSync)
            {
                _model.Text = NoteTextBox.Text;
                Services.StickyNoteSync.MarkDirty(_model);
            }
        }
        protected override void OnClosed(EventArgs e)
        {
            // Static event: unsubscribe so closed notes can be garbage collected
            Wpf.Ui.Appearance.ApplicationThemeManager.Changed -= OnAppThemeChanged;
            if (_hwnd != IntPtr.Zero) RemoveWindowSubclass(_hwnd, _keepBackdropProc, UIntPtr.Zero);
            base.OnClosed(e);

            // 1. Find the main flyout window to check its status
            var mainWindow = System.Windows.Application.Current.Windows.OfType<TasksFlyoutWindow>().FirstOrDefault();

            // 2. Check if the main window is hidden in the system tray or minimized
            bool isMainHidden = mainWindow == null || !mainWindow.IsVisible || mainWindow.WindowState == WindowState.Minimized;

            // 3. Count how many sticky notes are currently open (excluding this one that is closing)
            int openNotes = System.Windows.Application.Current.Windows.OfType<StickyNoteWindow>().Count(w => w != this);

            // 4. If the main app is hidden and no other notes are left, flush the memory
            if (isMainHidden && openNotes == 0)
            {
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
                }
                catch { }
            }
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}