using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using TasksBar.Linux.Services;
using TasksBar.Services;

namespace TasksBar.Linux.Views
{
    /// <summary>
    /// A sticky note, ported from the Windows app's StickyNoteWindow: text, pen and eraser, pasted
    /// images, the ⋯ menu, drag to move, double-click for full screen, and sync of everything.
    /// </summary>
    public partial class StickyNoteWindow : Window
    {
        private static readonly List<StickyNoteWindow> OpenWindows = new();

        private readonly StickyNoteModel _model;
        private bool _suppressSync;
        private string _shownImages = "";
        private readonly DispatcherTimer _savePositionTimer;
        private bool _placed;

        public StickyNoteModel GetModel() => _model;

        public static StickyNoteWindow? Find(StickyNoteModel note) => OpenWindows.FirstOrDefault(w => w._model == note);

        public static IReadOnlyList<StickyNoteWindow> All => OpenWindows;

        /// <summary>Opens an existing note where it was last left.</summary>
        public StickyNoteWindow(StickyNoteModel model)
        {
            _model = model;
            InitializeComponent();
            _savePositionTimer = CreateSaveTimer();
            Wire();

            PlaceAt(_model.Left, _model.Top);
            _suppressSync = true;
            NoteTextBox.Text = _model.Text;
            _suppressSync = false;
            UpdateFlowDirection();

            LoadInk();
            LoadImages();
        }

        /// <summary>Creates a brand new note at this spot (in device-independent pixels, like the Windows app).</summary>
        public StickyNoteWindow(double startLeft, double startTop)
        {
            _model = new StickyNoteModel { Left = startLeft, Top = startTop };
            InitializeComponent();
            _savePositionTimer = CreateSaveTimer();
            Wire();

            PlaceAt(startLeft, startTop);
            LocalDataManager.ActiveNotes.Add(_model);
            StickyNoteSync.MarkDirty(_model);
            LoadInk();
        }

        private void Wire()
        {
            OpenWindows.Add(this);
            Backdrop.Attach(this, Chrome);

            NoteTextBox.TextChanged += NoteTextBox_TextChanged;
            Ink.StrokesChanged += SaveInk;
            ActualThemeVariantChanged += (s, e) => UpdateInkColor();
            UpdateInkColor();

            RootGrid.PointerPressed += RootGrid_PointerPressed;
            RootGrid.PointerMoved += RootGrid_PointerMoved;
            RootGrid.PointerReleased += (s, e) => _maximizedGrab = null;
            AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);

            ResizeFrom(EdgeLeft, WindowEdge.West);
            ResizeFrom(EdgeRight, WindowEdge.East);
            ResizeFrom(EdgeTop, WindowEdge.North);
            ResizeFrom(EdgeBottom, WindowEdge.South);
            ResizeFrom(CornerTopLeft, WindowEdge.NorthWest);
            ResizeFrom(CornerTopRight, WindowEdge.NorthEast);
            ResizeFrom(CornerBottomLeft, WindowEdge.SouthWest);
            ResizeFrom(ResizeGrip, WindowEdge.SouthEast);

            PositionChanged += (s, e) =>
            {
                if (!_placed || WindowState != WindowState.Normal) return;
                _savePositionTimer.Stop();
                _savePositionTimer.Start();
            };
            this.GetObservable(WindowStateProperty).Subscribe(new SimpleObserver<WindowState>(OnWindowStateChanged));
            Opened += (s, e) => _placed = true;
        }

        // ------------------------------------------------------------------ position and size

        private double Scaling => (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.Scaling ?? 1;

        private void PlaceAt(double left, double top)
        {
            double scaling = Screens.Primary?.Scaling ?? 1;
            Position = new PixelPoint((int)Math.Round(left * scaling), (int)Math.Round(top * scaling));
        }

        /// <summary>Puts a note that arrived from another device near the top-right corner, stacked by <paramref name="index"/>.</summary>
        public void CascadeFromTopRight(int index)
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen == null) return;
            var area = screen.WorkingArea;
            double scaling = screen.Scaling;
            int width = (int)(Bounds.Width * scaling);
            int x = Math.Max(area.X, area.Right - width - (int)((40 + 30 * index) * scaling));
            int y = area.Y + (int)((40 + 30 * index) * scaling);
            Position = new PixelPoint(x, y);
            _model.Left = x / scaling;
            _model.Top = y / scaling;
            LocalDataManager.SaveNotes();
        }

        // Positions stay on this device (they aren't synced), so they're only saved to disk
        private DispatcherTimer CreateSaveTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                double scaling = Scaling;
                _model.Left = Math.Round(Position.X / scaling, 1);
                _model.Top = Math.Round(Position.Y / scaling, 1);
                LocalDataManager.SaveNotes();
            };
            return timer;
        }

        private void ResizeFrom(Control handle, WindowEdge edge)
        {
            handle.PointerPressed += (s, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || WindowState != WindowState.Normal) return;
                e.Handled = true;
                BeginResizeDrag(edge, e);
            };
        }

        private void OnWindowStateChanged(WindowState state)
        {
            // Edges and grip only make sense at the normal size
            bool normal = state == WindowState.Normal;
            foreach (var edge in new Control[] { EdgeLeft, EdgeRight, EdgeTop, EdgeBottom, CornerTopLeft, CornerTopRight, CornerBottomLeft, ResizeGrip })
                edge.IsVisible = normal;
            Chrome.CornerRadius = new CornerRadius(normal ? 8 : 0);
        }

        private Point? _maximizedGrab;
        private PointerPressedEventArgs? _maximizedPress;
        private Size _normalSize = new Size(250, 250);

        private void RootGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            // Double-click toggles full screen, like a normal title bar
            if (e.ClickCount == 2)
            {
                _maximizedGrab = null;
                ToggleFullScreen();
                e.Handled = true;
                return;
            }

            // A full-screen note only restores once it's actually dragged (so a double-click doesn't restore it early)
            if (WindowState == WindowState.Maximized)
            {
                _maximizedGrab = e.GetPosition(this);
                _maximizedPress = e;
                return;
            }

            BeginMoveDrag(e);
        }

        private void RootGrid_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_maximizedGrab is not Point grab || _maximizedPress == null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _maximizedGrab = null;
                return;
            }

            var delta = e.GetPosition(this) - grab;
            if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;

            // Pop back to the normal size under the cursor, then keep dragging like a title bar
            _maximizedGrab = null;
            double ratioX = Bounds.Width > 0 ? grab.X / Bounds.Width : 0.5;
            var screenPoint = this.PointToScreen(e.GetPosition(this));
            double scaling = Scaling;
            WindowState = WindowState.Normal;
            Width = _normalSize.Width;
            Height = _normalSize.Height;
            Position = new PixelPoint(
                (int)(screenPoint.X - _normalSize.Width * ratioX * scaling),
                (int)(screenPoint.Y - Math.Min(grab.Y, _normalSize.Height / 2) * scaling));
            BeginMoveDrag(_maximizedPress);
        }

        private void ToggleFullScreen()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                return;
            }
            _normalSize = Bounds.Size;
            WindowState = WindowState.Maximized;
        }

        // ------------------------------------------------------------------ text

        private void NoteTextBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            UpdateFlowDirection();
            // Avalonia also raises this when the box first shows its text, which isn't an edit
            string text = NoteTextBox.Text ?? "";
            if (_suppressSync || text == (_model.Text ?? "")) return;
            _model.Text = text;
            StickyNoteSync.MarkDirty(_model);
        }

        // Hebrew/Arabic text reads right to left, like the Windows app's AutoRtlConverter
        private void UpdateFlowDirection() =>
            NoteTextBox.FlowDirection = FlyoutWindow.HasRtl(NoteTextBox.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // ------------------------------------------------------------------ pen and eraser

        private void ModeButton_Click(object? sender, RoutedEventArgs e)
        {
            TextModeBtn.IsChecked = sender == TextModeBtn;
            PenModeBtn.IsChecked = sender == PenModeBtn;
            EraserModeBtn.IsChecked = sender == EraserModeBtn;

            Ink.EditingMode = sender == PenModeBtn ? InkLayer.Mode.Ink
                                : sender == EraserModeBtn ? InkLayer.Mode.Erase
                                : InkLayer.Mode.None;
        }

        private void LoadInk()
        {
            // Only the Windows app can read drawings saved by its older versions. Leave those untouched
            // (drawing here would replace them) until Windows converts them to the shared format.
            bool legacy = NoteInk.IsLegacy(_model.InkData);
            LegacyInkHint.IsVisible = legacy;
            PenModeBtn.IsEnabled = !legacy;
            EraserModeBtn.IsEnabled = !legacy;
            if (legacy && Ink.EditingMode != InkLayer.Mode.None) ModeButton_Click(TextModeBtn, new RoutedEventArgs());

            Ink.Load(legacy ? null : _model.InkData);
        }

        private void SaveInk()
        {
            if (_suppressSync) return;
            _model.InkData = Ink.Save();
            StickyNoteSync.MarkDirty(_model);
        }

        // Ink is white on dark notes and black on light ones, like the Windows app
        private void UpdateInkColor()
        {
            Ink.InkBrush = ActualThemeVariant == ThemeVariant.Dark ? Brushes.White : Brushes.Black;
            Ink.InvalidateVisual();
        }

        // ------------------------------------------------------------------ pasted images

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            bool isPaste = (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control) ||
                           (e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.Shift);
            if (!isPaste || Clipboard == null) return;

            // The clipboard is read asynchronously, so decide afterwards: text pastes into the note as usual
            e.Handled = true;
            _ = PasteAsync();
        }

        private async Task PasteAsync()
        {
            var clipboard = Clipboard!;
#pragma warning disable CS0618
            Console.WriteLine("DBG formats: " + string.Join(", ", await clipboard.GetFormatsAsync())); // DEBUG-TEMP
#pragma warning restore CS0618
            if (await NoteImageBitmaps.HasTextAsync(clipboard))
            {
                if (NoteTextBox.IsFocused) NoteTextBox.Paste();
                return;
            }

            var bytes = await NoteImageBitmaps.GetClipboardImageAsync(clipboard);
            if (bytes != null) AddImage(bytes);
        }

        private void AddImage(byte[] encoded)
        {
            (string Id, int Width, int Height)? saved;
            try
            {
                saved = NoteImageBitmaps.SaveFromEncoded(encoded);
            }
            catch (Exception ex)
            {
                _ = MessageWindow.ShowAsync("TasksBar", $"Couldn't add the image: {ex.Message}");
                return;
            }
            if (saved is not { } image) return;

            // Show it at its real on-screen size, shrunk to fit the note
            double width = image.Width / RenderScaling;
            double maxWidth = Math.Max(60, ImageLayer.Bounds.Width - 16);
            if (width > maxWidth) width = maxWidth;
            double height = width * image.Height / Math.Max(1, image.Width);

            // Cascade so several pastes don't stack exactly on top of each other
            double offset = 8 + 16 * (_model.Images.Count % 5);
            var model = new NoteImage
            {
                Id = image.Id,
                X = offset,
                Y = offset,
                Width = Math.Round(width, 1),
                Height = Math.Round(height, 1)
            };

            _model.Images.Add(model);
            ImageLayer.Children.Add(new NoteImageView(model, OnImagesChanged, RemoveImage));
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
            if (_suppressSync) return;
            StickyNoteSync.MarkDirty(_model);
        }

        // Rebuilds the image views, unless the same images are already shown (so a sync never interrupts a drag)
        private void LoadImages()
        {
            string signature = string.Join("|", _model.Images.Select(i =>
                $"{i.Id},{i.X},{i.Y},{i.Width},{i.Height},{i.Rotation},{NoteImageStore.Exists(i.Id)}"));
            if (signature == _shownImages && ImageLayer.Children.Count == _model.Images.Count) return;
            _shownImages = signature;

            ImageLayer.Children.Clear();
            foreach (var image in _model.Images)
            {
                ImageLayer.Children.Add(new NoteImageView(image, OnImagesChanged, RemoveImage));
            }
        }

        /// <summary>Shows content that sync pulled from another device, without re-uploading it.</summary>
        public void ReloadFromModel()
        {
            _suppressSync = true;
            try
            {
                if (NoteTextBox.Text != _model.Text) NoteTextBox.Text = _model.Text;
                LoadInk();
                LoadImages();
            }
            finally
            {
                _suppressSync = false;
            }
        }

        // ------------------------------------------------------------------ menu, close, delete

        private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            OpenWindows.Remove(this);
            _savePositionTimer.Stop();

            // Like the Windows app, give memory back once nothing is showing
            bool flyoutHidden = (Application.Current as App)?.Flyout is not { IsVisible: true };
            if (flyoutHidden && OpenWindows.Count == 0) GC.Collect();
        }

        private void NoteMenuButton_Click(object? sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedLeft };

            var startupItem = new MenuItem
            {
                Header = "Open when TasksBar starts",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _model.OpenOnStartup
            };
            startupItem.Click += (s, args) =>
            {
                _model.OpenOnStartup = !_model.OpenOnStartup;
                startupItem.IsChecked = _model.OpenOnStartup;
                StickyNoteSync.MarkDirty(_model);
                NoteStartupChanged?.Invoke(_model);
            };
            menu.Items.Add(startupItem);
            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem
            {
                Header = "Delete note",
                Icon = new SymbolIcon { Symbol = Symbol.Delete, FontSize = 16 }
            };
            // Unlike closing, this removes the note everywhere it syncs to
            deleteItem.Click += (s, args) => DeleteNote(_model);
            menu.Items.Add(deleteItem);

            menu.Open(NoteMenuButton);
        }

        /// <summary>Removes a note for good: locally, from the sync service, and closes its window.</summary>
        public static void DeleteNote(StickyNoteModel note)
        {
            LocalDataManager.ActiveNotes.Remove(note);
            LocalDataManager.SaveNotes();
            _ = StickyNoteSync.DeleteNoteAsync(note);

            foreach (var window in OpenWindows.Where(w => w._model == note).ToList()) window.Close();

            NoteDeleted?.Invoke(note);
        }

        public static event Action<StickyNoteModel>? NoteDeleted;

        /// <summary>Raised when a note's "open at startup" setting is changed from its own menu.</summary>
        public static event Action<StickyNoteModel>? NoteStartupChanged;

        private sealed class SimpleObserver<T> : IObserver<T>
        {
            private readonly Action<T> _next;
            public SimpleObserver(Action<T> next) => _next = next;
            public void OnNext(T value) => _next(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
