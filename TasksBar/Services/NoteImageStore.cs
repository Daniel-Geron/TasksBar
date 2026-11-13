using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TasksBar.Services
{
    /// <summary>
    /// Stores the pixels of images pasted into sticky notes. Images are shrunk and saved as JPEG
    /// (small enough to sync through Google Tasks) under note_images/, named by a hash of their bytes.
    /// </summary>
    public static class NoteImageStore
    {
        private const int MaxPixelSize = 1000;
        private const int JpegQuality = 82;

        private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "note_images");

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

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

        /// <summary>Loads an image for display without keeping the file locked.</summary>
        public static BitmapImage? Load(string id)
        {
            if (!Exists(id)) return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(ReadBytes(id));
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { return null; }
        }

        /// <summary>Shrinks the image to at most 1000px, flattens transparency onto white and saves it as JPEG.</summary>
        public static string SaveFromBitmap(BitmapSource source)
        {
            double scale = Math.Min(1.0, (double)MaxPixelSize / Math.Max(source.PixelWidth, source.PixelHeight));
            int width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
            int height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, width, height);
                dc.DrawRectangle(Brushes.White, null, rect);
                dc.DrawImage(source, rect);
            }

            var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);

            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return SaveBytes(ms.ToArray());
        }

        /// <summary>
        /// Reads an image from the clipboard: PNG data (screenshots, browsers), a copied image file,
        /// or a plain bitmap. Returns null when the clipboard holds no image.
        /// </summary>
        public static BitmapSource? GetClipboardImage()
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return null;

                if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
                {
                    return BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                }

                if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    string? file = files.FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    if (file != null)
                    {
                        using var stream = new MemoryStream(File.ReadAllBytes(file));
                        return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    }
                }

                // WPF's own Clipboard.GetImage mangles some bitmaps, so go through WinForms and PNG
                using var bitmap = System.Windows.Forms.Clipboard.GetImage();
                if (bitmap != null)
                {
                    using var ms = new MemoryStream();
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    return BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reading clipboard image failed: {ex.Message}");
            }
            return null;
        }

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
