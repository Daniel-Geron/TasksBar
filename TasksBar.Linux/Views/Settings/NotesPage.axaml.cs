using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using TasksBar.Services;

namespace TasksBar.Linux.Views.Settings
{
    /// <summary>
    /// Every sticky note: rename it (✏️ or double-click the title), choose whether it opens when
    /// TasksBar starts, open it, or delete it. Same as the Windows app's Sticky Notes page.
    /// </summary>
    public partial class NotesPage : UserControl
    {
        private bool _updatingToggle;

        public NotesPage()
        {
            InitializeComponent();
            Rebuild();

            // Notes can also be deleted or changed from their own window while this page is open
            AttachedToVisualTree += (s, e) =>
            {
                StickyNoteWindow.NoteDeleted += OnNotesChanged;
                StickyNoteWindow.NoteStartupChanged += OnNotesChanged;
            };
            DetachedFromVisualTree += (s, e) =>
            {
                StickyNoteWindow.NoteDeleted -= OnNotesChanged;
                StickyNoteWindow.NoteStartupChanged -= OnNotesChanged;
            };
        }

        private void OnNotesChanged(StickyNoteModel note) => Rebuild();

        private void Rebuild()
        {
            NotesList.Children.Clear();
            foreach (var note in LocalDataManager.ActiveNotes) NotesList.Children.Add(CreateRow(note));
            EmptyText.IsVisible = LocalDataManager.ActiveNotes.Count == 0;
            UpdateOpenAllToggle();
        }

        // The top switch is on only when every note opens at startup
        private void UpdateOpenAllToggle()
        {
            var notes = LocalDataManager.ActiveNotes;
            int opening = notes.Count(n => n.OpenOnStartup);
            _updatingToggle = true;
            OpenAllToggle.IsChecked = notes.Count > 0 && opening == notes.Count;
            _updatingToggle = false;
            OpenAllCard.Description = $"{opening} of {notes.Count} notes open automatically";
        }

        private void OpenAllToggle_Changed(object? sender, RoutedEventArgs e)
        {
            if (_updatingToggle) return;
            bool openAll = OpenAllToggle.IsChecked == true;
            foreach (var note in LocalDataManager.ActiveNotes.Where(n => n.OpenOnStartup != openAll))
            {
                note.OpenOnStartup = openAll;
                StickyNoteSync.MarkDirty(note);
            }
            Rebuild();
        }

        private Control CreateRow(StickyNoteModel note)
        {
            // Title (with rename button) and the line under it
            var title = new TextBlock
            {
                Text = note.DisplayTitle,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(title, "Double-click to rename");
            var renameButton = IconButton(Symbol.Edit, "Rename", 16);
            renameButton.Classes.Add("flat");
            renameButton.Padding = new Thickness(4);
            renameButton.Margin = new Thickness(4, 0, 0, 0);

            // The title shrinks (with "…") before the rename button is pushed out
            var titleView = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), HorizontalAlignment = HorizontalAlignment.Left };
            titleView.Children.Add(title);
            Grid.SetColumn(renameButton, 1);
            titleView.Children.Add(renameButton);

            var editor = new TextBox { IsVisible = false, MinWidth = 240, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, Watermark = note.DefaultTitle };
            var titleHost = new Panel { Children = { titleView, editor } };

            var preview = new TextBlock { Text = note.Preview, FontSize = 12, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            preview.IsVisible = !string.IsNullOrEmpty(preview.Text);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Left, Children = { titleHost, preview } };

            void BeginRename()
            {
                // Start from the current title (or the default one, so it's easy to tweak)
                editor.Text = note.DisplayTitle;
                titleView.IsVisible = false;
                editor.IsVisible = true;
                editor.Focus();
                editor.SelectAll();
            }

            bool renaming = false;
            void EndRename(bool save)
            {
                if (renaming) return;
                renaming = true;
                editor.IsVisible = false;
                titleView.IsVisible = true;
                renaming = false;
                if (!save) return;

                // Clearing the box (or leaving the default unchanged) goes back to the automatic title
                string newTitle = StickyNoteSync.CleanTitle(editor.Text);
                if (newTitle == note.DefaultTitle && string.IsNullOrEmpty(note.Title)) newTitle = "";
                if (newTitle == (note.Title ?? "")) return;
                note.Title = newTitle;
                StickyNoteSync.MarkDirty(note);
                title.Text = note.DisplayTitle;
                preview.Text = note.Preview;
                preview.IsVisible = !string.IsNullOrEmpty(preview.Text);
            }

            renameButton.Click += (s, e) => BeginRename();
            title.DoubleTapped += (s, e) => BeginRename();
            editor.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { EndRename(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { EndRename(false); e.Handled = true; }
            };
            editor.LostFocus += (s, e) => { if (editor.IsVisible) EndRename(true); };

            // Right side: open-at-startup switch, Open, Delete
            var startupToggle = new ToggleSwitch { IsChecked = note.OpenOnStartup, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            startupToggle.IsCheckedChanged += (s, e) =>
            {
                if (startupToggle.IsChecked == note.OpenOnStartup) return;
                note.OpenOnStartup = startupToggle.IsChecked == true;
                StickyNoteSync.MarkDirty(note);
                UpdateOpenAllToggle();
            };

            var open = new Button
            {
                Margin = new Thickness(0, 0, 8, 0),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { new SymbolIcon { Symbol = Symbol.Open, FontSize = 16, Width = 16, Height = 16 }, new TextBlock { Text = "Open" } }
                }
            };
            open.Click += (s, e) =>
            {
                // Bring it to the front if it's already open, instead of opening it twice
                var window = StickyNoteWindow.Find(note);
                if (window == null)
                {
                    window = new StickyNoteWindow(note);
                    window.Show();
                }
                window.Activate();
            };

            var delete = IconButton(Symbol.Delete, "Delete note", 16);
            delete.Click += (s, e) => StickyNoteWindow.DeleteNote(note);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "Open at startup", FontSize = 12, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                    startupToggle,
                    open,
                    delete
                }
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(texts);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            var card = new Border { Child = grid, Margin = new Thickness(0, 0, 0, 8) };
            card.Classes.Add("card");
            return card;
        }

        private static Button IconButton(Symbol symbol, string tip, double size)
        {
            var button = new Button { Content = new SymbolIcon { Symbol = symbol, FontSize = size, Width = size, Height = size } };
            ToolTip.SetTip(button, tip);
            return button;
        }
    }
}
