using System;
using TasksBar.Linux.Views;
using TasksBar.Services;

namespace TasksBar.Linux.Services
{
    /// <summary>Connects the shared sticky note sync (TasksBar.Core) to the Linux note windows.</summary>
    public class AvaloniaStickyNoteHost : IStickyNoteHost
    {
        // Drawings in the old WPF format can only be read on Windows; keep them rather than dropping them
        public bool HasLegacyStrokes(string ink) => true;

        public void ReloadNote(StickyNoteModel note) => StickyNoteWindow.Find(note)?.ReloadFromModel();

        public void OpenArrivedNote(StickyNoteModel note, int cascadeIndex)
        {
            var window = StickyNoteWindow.Find(note);
            if (window != null)
            {
                window.ReloadFromModel();
                return;
            }

            window = new StickyNoteWindow(note);
            window.Show();
            window.CascadeFromTopRight(cascadeIndex);
        }

        public void CloseNote(StickyNoteModel note) => StickyNoteWindow.Find(note)?.Close();
    }
}
