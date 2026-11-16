using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace TasksBar.Services
{
    /// <summary>
    /// Stores the pixels of images pasted into sticky notes. Images are shrunk and saved as JPEG
    /// (small enough to sync through Google Tasks) under note_images/, named by a hash of their bytes.
    /// Decoding and encoding live with each app's UI (NoteImageBitmaps in the Windows app).
    /// </summary>
    public static class NoteImageStore
    {
        public const int MaxPixelSize = 1000;
        public const int JpegQuality = 82;

        public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

        private static string Folder => AppPaths.File("note_images");

        public static string GetPath(string id) => Path.Combine(Folder, id + ".jpg");

        public static bool Exists(string id) => File.Exists(GetPath(id));

        public static byte[] ReadBytes(string id) => File.ReadAllBytes(GetPath(id));

        /// <summary>Saves already-compressed JPEG bytes (e.g. downloaded from Google). Returns the image id.</summary>
        public static string SaveBytes(byte[] jpeg)
        {
            string id = HashOf(jpeg);
            Directory.CreateDirectory(Folder);
            if (!Exists(id)) File.WriteAllBytes(GetPath(id), jpeg);
            return id;
        }

        public static string HashOf(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 20).ToLowerInvariant();

        /// <summary>Deletes image files no note uses anymore.</summary>
        public static void RemoveUnused(IEnumerable<StickyNoteModel> notes)
        {
            if (!Directory.Exists(Folder)) return;
            var used = new HashSet<string>(notes.SelectMany(n => n.Images).Select(i => i.Id));
            foreach (var file in Directory.GetFiles(Folder, "*.jpg"))
            {
                if (used.Contains(Path.GetFileNameWithoutExtension(file))) continue;
                try { File.Delete(file); } catch { }
            }
        }
    }
}
