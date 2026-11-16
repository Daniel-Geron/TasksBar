using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace TasksBar
{
    public partial class NotesPage : Page
    {
        // This allows the list to visually update when we delete something
        public ObservableCollection<StickyNoteModel> DisplayNotes { get; set; }

        public NotesPage()
        {
            InitializeComponent();

            // Load the list directly from our central data manager
            DisplayNotes = new ObservableCollection<StickyNoteModel>(LocalDataManager.ActiveNotes);
            NotesListView.ItemsSource = DisplayNotes;
            UpdateOpenAllToggle();

            // Notes can also be deleted from their own window while this page is open
            Loaded += (s, e) =>
            {
                StickyNoteWindow.NoteDeleted += OnNoteDeleted;
                StickyNoteWindow.NoteStartupChanged += OnNoteStartupChanged;
            };
            Unloaded += (s, e) =>
            {
                StickyNoteWindow.NoteDeleted -= OnNoteDeleted;
                StickyNoteWindow.NoteStartupChanged -= OnNoteStartupChanged;
            };
        }

        private void OnNoteStartupChanged(StickyNoteModel note)
        {
            NotesListView.Items.Refresh();
            UpdateOpenAllToggle();
        }

        // The top switch is on only when every note opens at startup
        private void UpdateOpenAllToggle()
        {
            int opening = DisplayNotes.Count(n => n.OpenOnStartup);
            OpenAllToggle.IsChecked = DisplayNotes.Count > 0 && opening == DisplayNotes.Count;
            OpenAllSummaryText.Text = $"{opening} of {DisplayNotes.Count} notes open automatically";
        }

        private void OpenOnStartup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.ToggleSwitch toggle && toggle.Tag is StickyNoteModel note)
            {
                note.OpenOnStartup = toggle.IsChecked == true;
                Services.StickyNoteSync.MarkDirty(note);
                UpdateOpenAllToggle();
            }
        }

        private void OpenAllToggle_Click(object sender, RoutedEventArgs e)
        {
            bool openAll = OpenAllToggle.IsChecked == true;

            foreach (var note in DisplayNotes.Where(n => n.OpenOnStartup != openAll))
            {
                note.OpenOnStartup = openAll;
                Services.StickyNoteSync.MarkDirty(note);
            }

            NotesListView.Items.Refresh();
            UpdateOpenAllToggle();
        }

        private void OpenNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is StickyNoteModel note)
            {
                // PREVENT DUPLICATES: Check if this specific note is already open on the screen
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is StickyNoteWindow sticky && sticky.GetModel() == note)
                    {
                        // It's already open, so just bring it to the front!
                        sticky.Focus();
                        return;
                    }
                }

                // If we get here, it wasn't open, so spawn it!
                var newNoteWindow = new StickyNoteWindow(note);
                newNoteWindow.Show();
            }
        }

        // --- RENAMING ---

        private void RenameNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element) BeginRename(element);
        }

        private void Title_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element)
            {
                BeginRename(element);
                e.Handled = true;
            }
        }

        // The title view and its editor share a Grid in the card's template
        private static (FrameworkElement? View, Wpf.Ui.Controls.TextBox? Editor) FindTitleParts(DependencyObject from)
        {
            for (var current = from; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            {
                if (current is Grid grid)
                {
                    var view = grid.Children.OfType<Grid>().FirstOrDefault(c => c.Name == "TitleView");
                    var editor = grid.Children.OfType<Wpf.Ui.Controls.TextBox>().FirstOrDefault(c => c.Name == "TitleEditor");
                    if (view != null && editor != null) return (view, editor);
                }
            }
            return (null, null);
        }

        private void BeginRename(FrameworkElement source)
        {
            var (view, editor) = FindTitleParts(source);
            if (view == null || editor == null || editor.Tag is not StickyNoteModel note) return;

            // Start from the current title (or the default one, so it's easy to tweak)
            editor.Text = note.DisplayTitle;
            view.Visibility = Visibility.Collapsed;
            editor.Visibility = Visibility.Visible;
            editor.Focus();
            editor.SelectAll();
        }

        private void TitleEditor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.TextBox editor) return;

            if (e.Key == System.Windows.Input.Key.Enter)
            {
                EndRename(editor, save: true);
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                EndRename(editor, save: false);
                e.Handled = true;
            }
        }

        private void TitleEditor_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.TextBox editor && editor.Visibility == Visibility.Visible)
                EndRename(editor, save: true);
        }

        private void EndRename(Wpf.Ui.Controls.TextBox editor, bool save)
        {
            var (view, _) = FindTitleParts(editor);
            editor.Visibility = Visibility.Collapsed;
            if (view != null) view.Visibility = Visibility.Visible;

            if (!save || editor.Tag is not StickyNoteModel note) return;

            // Clearing the box (or leaving the default unchanged) goes back to the automatic title
            string title = Services.StickyNoteSync.CleanTitle(editor.Text);
            if (title == note.DefaultTitle && string.IsNullOrEmpty(note.Title)) title = "";
            if (title == (note.Title ?? "")) return;

            note.Title = title;
            Services.StickyNoteSync.MarkDirty(note);
            NotesListView.Items.Refresh();
        }

        private void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is StickyNoteModel note)
            {
                StickyNoteWindow.DeleteNote(note);
            }
        }

        private void OnNoteDeleted(StickyNoteModel note)
        {
            DisplayNotes.Remove(note);
            UpdateOpenAllToggle();
        }
    }
}