using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace TasksBar
{
    public partial class StickyNoteWindow : FluentWindow
    {
        private StickyNoteModel _model;
        public StickyNoteModel GetModel() => _model;

        // Constructor for loading EXISTING notes
        public StickyNoteWindow(StickyNoteModel model)
        {
            InitializeComponent();
            _model = model;
            ApplyThemeSettings();

            this.Left = _model.Left;
            this.Top = _model.Top;
            NoteTextBox.Text = _model.Text;

            LoadInk();
            UpdateInkColor();
            // Auto-save whenever a line is drawn or erased
            NoteInkCanvas.Strokes.StrokesChanged += (s, e) => SaveInk();
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
            LocalDataManager.SaveNotes();
            UpdateInkColor();
            NoteInkCanvas.Strokes.StrokesChanged += (s, e) => SaveInk();
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

        private void SaveInk()
        {
            if (_model == null) return;

            using (MemoryStream ms = new MemoryStream())
            {
                NoteInkCanvas.Strokes.Save(ms);
                _model.InkData = Convert.ToBase64String(ms.ToArray());
                LocalDataManager.SaveNotes();
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
            if (_model != null)
            {
                _model.Text = NoteTextBox.Text;
                LocalDataManager.SaveNotes();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}