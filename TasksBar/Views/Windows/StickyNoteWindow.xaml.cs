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
            LoadImages();
            // Auto-save whenever a line is drawn or erased
            NoteInkCanvas.Strokes.StrokesChanged += OnStrokesChanged;
            this.StateChanged += Window_StateChanged;
            this.PreviewKeyDown += Window_PreviewKeyDown;
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
            this.PreviewKeyDown += Window_PreviewKeyDown;
        }

        // --- PASTED IMAGES ---

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool isPaste = (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) ||
                           (e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.Shift);
            if (!isPaste) return;

            // Text on the clipboard pastes into the note as usual
            if (Clipboard.ContainsText()) return;

            var bitmap = Services.NoteImageStore.GetClipboardImage();
            if (bitmap == null) return;

            e.Handled = true;
            AddImage(bitmap);
        }

        private void AddImage(System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            string id;
            try
            {
                id = Services.NoteImageStore.SaveFromBitmap(bitmap);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Couldn't add the image: {ex.Message}", "TasksBar");
                return;
            }

            // Show it at its real on-screen size, shrunk to fit the note
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            double width = bitmap.PixelWidth / dpi.DpiScaleX;
            double maxWidth = Math.Max(60, ImageLayer.ActualWidth - 16);
            if (width > maxWidth) width = maxWidth;
            double height = width * bitmap.PixelHeight / Math.Max(1, bitmap.PixelWidth);

            // Cascade so several pastes don't stack exactly on top of each other
            double offset = 8 + 16 * (_model.Images.Count % 5);
            var image = new NoteImage
            {
                Id = id,
                X = offset,
                Y = offset,
                Width = Math.Round(width, 1),
                Height = Math.Round(height, 1)
            };

            _model.Images.Add(image);
            ImageLayer.Children.Add(new NoteImageView(image, OnImagesChanged, RemoveImage));
            OnImagesChanged();
        }

        private void RemoveImage(NoteImageView view)
        {
            _model.Images.Remove(view.Model);
            ImageLayer.Children.Remove(view);
            OnImagesChanged();
        }

        private void OnImagesChanged()
        {
            if (_model == null || _suppressSync) return;
            Services.StickyNoteSync.MarkDirty(_model);
        }

        private string _shownImages = "";

        // Rebuilds the image views, unless the same images are already shown (so a sync never interrupts a drag)
        private void LoadImages()
        {
            string signature = string.Join("|", _model.Images.Select(i =>
                $"{i.Id},{i.X},{i.Y},{i.Width},{i.Height},{i.Rotation},{Services.NoteImageStore.Exists(i.Id)}"));
            if (signature == _shownImages && ImageLayer.Children.Count == _model.Images.Count) return;
            _shownImages = signature;

            ImageLayer.Children.Clear();
            foreach (var image in _model.Images)
            {
                ImageLayer.Children.Add(new NoteImageView(image, OnImagesChanged, RemoveImage));
            }
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
            // The app-wide theme is applied once by the flyout (and by Settings when it changes).
            // Re-applying it here reloaded every theme resource and knocked the backdrop off the
            // other open windows, so a note only sets its own backdrop.
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
                LoadImages();
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

        private void NoteMenuButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = NoteMenuButton,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

            var deleteItem = new System.Windows.Controls.MenuItem
            {
                Header = "Delete note",
                Icon = new SymbolIcon(SymbolRegular.Delete16)
            };
            // Unlike closing, this removes the note everywhere it syncs to
            deleteItem.Click += (s, args) => DeleteNote(_model);
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
        }

        /// <summary>Removes a note for good: locally, from Google, and closes its window.</summary>
        public static void DeleteNote(StickyNoteModel note)
        {
            LocalDataManager.ActiveNotes.Remove(note);
            LocalDataManager.SaveNotes();
            _ = Services.StickyNoteSync.DeleteNoteAsync(note);

            foreach (var window in Application.Current.Windows.OfType<StickyNoteWindow>().ToList())
            {
                if (window.GetModel() == note) window.Close();
            }

            // Keep an open Settings > Sticky Notes page in step
            NoteDeleted?.Invoke(note);
        }

        public static event Action<StickyNoteModel>? NoteDeleted;
    }
}