using System;
using System.Linq;
using System.Windows;

namespace TasksBar.Services
{
    /// <summary>Connects the shared sticky note sync (TasksBar.Core) to the WPF note windows.</summary>
    public class WpfStickyNoteHost : IStickyNoteHost
    {
        public bool HasLegacyStrokes(string ink) => (WpfInk.ReadLegacy(ink)?.Count ?? 0) > 0;

        public void ReloadNote(StickyNoteModel note) => FindWindow(note)?.ReloadFromModel();

        public void OpenArrivedNote(StickyNoteModel note, int cascadeIndex)
        {
            var window = FindWindow(note);
            if (window != null)
            {
                window.ReloadFromModel();
                return;
            }

            window = new StickyNoteWindow(note);
            window.Show();

            // Cascade new notes in from the top-right corner, sized by the real window
            var area = SystemParameters.WorkArea;
            window.Left = Math.Max(area.Left, area.Right - window.ActualWidth - 40 - 30 * cascadeIndex);
            window.Top = area.Top + 40 + 30 * cascadeIndex;
            note.Left = window.Left;
            note.Top = window.Top;
        }

        public void CloseNote(StickyNoteModel note) => FindWindow(note)?.Close();

        private static StickyNoteWindow? FindWindow(StickyNoteModel note) =>
            Application.Current.Windows.OfType<StickyNoteWindow>().FirstOrDefault(w => w.GetModel() == note);
    }
}
