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
            Loaded += (s, e) => StickyNoteWindow.NoteDeleted += OnNoteDeleted;
            Unloaded += (s, e) => StickyNoteWindow.NoteDeleted -= OnNoteDeleted;
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