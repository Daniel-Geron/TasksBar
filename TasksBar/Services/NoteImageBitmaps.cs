using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TasksBar.Services
{
    /// <summary>The WPF half of <see cref="NoteImageStore"/>: decoding, encoding and the clipboard.</summary>
    public static class NoteImageBitmaps
    {
        /// <summary>Loads an image for display without keeping the file locked.</summary>
        public static BitmapImage? Load(string id)
        {
            if (!NoteImageStore.Exists(id)) return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(NoteImageStore.ReadBytes(id));
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { return null; }
        }

        /// <summary>Shrinks the image to at most 1000px, flattens transparency onto white and saves it as JPEG.</summary>
        public static string SaveFromBitmap(BitmapSource source)
        {
            double scale = Math.Min(1.0, (double)NoteImageStore.MaxPixelSize / Math.Max(source.PixelWidth, source.PixelHeight));
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

            var encoder = new JpegBitmapEncoder { QualityLevel = NoteImageStore.JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return NoteImageStore.SaveBytes(ms.ToArray());
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
                    string? file = files.FirstOrDefault(f => NoteImageStore.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
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
    }
}
