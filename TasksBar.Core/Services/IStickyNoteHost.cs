namespace TasksBar.Services
{
    /// <summary>
    /// What sticky note sync needs from the app's UI. The Windows (WPF) and Linux (Avalonia) apps each
    /// implement it for their own note windows, and set <see cref="StickyNoteSync.Host"/>.
    /// </summary>
    public interface IStickyNoteHost
    {
        /// <summary>
        /// Whether drawing data in the old WPF ink format (base64 ISF, see <see cref="NoteInk"/>) holds at
        /// least one stroke. An app that can't read that format returns true, so the drawing is kept.
        /// </summary>
        bool HasLegacyStrokes(string ink);

        /// <summary>A note changed on another device: refresh its window if it's open.</summary>
        void ReloadNote(StickyNoteModel note);

        /// <summary>
        /// A new note arrived from another device and should open. <paramref name="cascadeIndex"/> counts
        /// the notes opened in this sync, so they can be stacked instead of covering each other.
        /// </summary>
        void OpenArrivedNote(StickyNoteModel note, int cascadeIndex);

        /// <summary>The note was deleted elsewhere: close its window if it's open.</summary>
        void CloseNote(StickyNoteModel note);
    }
}
