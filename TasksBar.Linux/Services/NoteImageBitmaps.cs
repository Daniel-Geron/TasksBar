using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;
using TasksBar.Services;

// The pre-11.3 clipboard API: its newer replacement is still settling, and this one reads X11 image formats reliably
#pragma warning disable CS0618

namespace TasksBar.Linux.Services
{
    /// <summary>The Linux half of <see cref="NoteImageStore"/>: decoding, JPEG encoding (with Skia) and the clipboard.</summary>
    public static class NoteImageBitmaps
    {
        public static Bitmap? Load(string id)
        {
            if (!NoteImageStore.Exists(id)) return null;
            try
            {
                using var ms = new MemoryStream(NoteImageStore.ReadBytes(id));
                return new Bitmap(ms);
            }
            catch { return null; }
        }

        /// <summary>
        /// Shrinks the image to at most 1000px, flattens transparency onto white and saves it as JPEG,
        /// exactly like the Windows app. Returns the image id and its size in pixels before shrinking.
        /// </summary>
        public static (string Id, int Width, int Height)? SaveFromEncoded(byte[] encoded)
        {
            using var source = SKBitmap.Decode(encoded);
            if (source == null || source.Width == 0 || source.Height == 0) return null;

            double scale = Math.Min(1.0, (double)NoteImageStore.MaxPixelSize / Math.Max(source.Width, source.Height));
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            {
                canvas.DrawBitmap(source, new SKRect(0, 0, width, height), paint);
            }
            using var image = surface.Snapshot();
            using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, NoteImageStore.JpegQuality);
            return (NoteImageStore.SaveBytes(jpeg.ToArray()), source.Width, source.Height);
        }

        private static readonly string[] ImageFormats = { "image/png", "PNG", "image/jpeg", "image/bmp", "image/webp", "image/gif" };

        /// <summary>
        /// Reads an image from the clipboard: image data (screenshots, browsers) or a copied image file.
        /// Returns null when the clipboard holds no image.
        /// </summary>
        public static async Task<byte[]?> GetClipboardImageAsync(IClipboard clipboard)
        {
            try
            {
                var formats = await clipboard.GetFormatsAsync();

                foreach (var format in ImageFormats)
                {
                    if (!formats.Contains(format)) continue;
                    if (await clipboard.GetDataAsync(format) is byte[] bytes && bytes.Length > 0) return bytes;
                }

                // A file copied in the file manager
                if (formats.Contains(DataFormats.Files) && await clipboard.GetDataAsync(DataFormats.Files) is System.Collections.Generic.IEnumerable<IStorageItem> items)
                {
                    foreach (var item in items)
                    {
                        string? path = item.TryGetLocalPath();
                        if (path != null && NoteImageStore.ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                            return await File.ReadAllBytesAsync(path);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reading clipboard image failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>Whether the clipboard holds text (which pastes into the note as usual).</summary>
        public static async Task<bool> HasTextAsync(IClipboard clipboard)
        {
            try
            {
                var formats = await clipboard.GetFormatsAsync();
                return formats.Contains(DataFormats.Text) || formats.Contains("UTF8_STRING") || formats.Contains("text/plain");
            }
            catch { return false; }
        }

        public static async Task CopyAsync(IClipboard clipboard, string id)
        {
            if (!NoteImageStore.Exists(id)) return;
            try
            {
                // Other apps expect PNG for pasted images
                using var bitmap = SKBitmap.Decode(NoteImageStore.ReadBytes(id));
                using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                var data = new DataObject();
                data.Set("image/png", png.ToArray());
                await clipboard.SetDataObjectAsync(data);
            }
            catch { }
        }
    }
}
