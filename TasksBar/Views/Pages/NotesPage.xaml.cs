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
                LocalDataManager.ActiveNotes.Remove(note);
                LocalDataManager.SaveNotes();

                DisplayNotes.Remove(note);

                // Added .Cast<Window>() so the compiler doesn't panic
                foreach (var window in Application.Current.Windows.Cast<Window>().OfType<StickyNoteWindow>().ToList())
                {
                    if (window.GetModel() == note)
                    {
                        window.Close();
                    }
                }
            }
        }
    }
}