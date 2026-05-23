using System;
using System.Windows;
using System.Windows.Controls;
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

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
                // Save new position after dragging
                _model.Left = this.Left;
                _model.Top = this.Top;
                LocalDataManager.SaveNotes();
            }
        }

        private void NoteTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Auto-save the JSON file every time a letter is typed!
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