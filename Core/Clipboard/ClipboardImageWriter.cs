using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Ghi ảnh PNG lên Clipboard dưới nhiều định dạng cùng lúc, để ứng dụng đích nào cũng nhận được:
    /// Word/Outlook đọc HTML Format, Office hiện đại đọc CF_DIBV5 (giữ kênh alpha),
    /// trình duyệt và công cụ đồ họa đọc định dạng "PNG", app cũ rơi về CF_BITMAP.
    ///
    /// Mặc định giữ nguyên các định dạng gốc trên clipboard để không phá thao tác dán khác của người dùng.
    /// </summary>
    public static class ClipboardImageWriter
    {
        private const string MarkerFormat = "HapiCopiProcessed";
        private const string PngFormat = "PNG";
        private const string DibV5Format = "Format17"; // CF_DIBV5
        private const string SvgMimeFormat = "image/svg+xml";

        private const int BitmapV5HeaderSize = 124;
        private const uint BI_BITFIELDS = 3;
        private const uint LCS_sRGB = 0x73524742;
        private const uint LCS_GM_IMAGES = 4;

        /// <summary>
        /// Đưa PNG lên clipboard kèm markup SVG gốc.
        /// </summary>
        /// <param name="pngBytes">Ảnh PNG đã rasterize.</param>
        /// <param name="svgMarkup">Markup SVG gốc, giữ lại để dán ngược vào công cụ vector.</param>
        /// <param name="originalFilePaths">Đường dẫn .svg gốc (nếu có) để giữ thao tác copy file.</param>
        /// <param name="replaceOriginal">true = thay hẳn clipboard bằng PNG; false = chỉ bổ sung.</param>
        /// <param name="displayWidth">Cỡ hiển thị mong muốn (px), tách khỏi độ phân giải thật của PNG.</param>
        public static bool SetPngWithSvgFallback(
            byte[] pngBytes,
            string svgMarkup,
            string[]? originalFilePaths,
            bool replaceOriginal,
            string suggestedName,
            int displayWidth,
            int displayHeight)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                bool staResult = false;
                var thread = new Thread(() => staResult = SetPngWithSvgFallback(
                    pngBytes, svgMarkup, originalFilePaths, replaceOriginal, suggestedName,
                    displayWidth, displayHeight));
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                return staResult;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                var disposables = new List<IDisposable>();
                try
                {
                    var dataObject = new DataObject();

                    // 1. Định dạng ảnh đặt trước để ứng dụng duyệt clipboard gặp ảnh trước văn bản.
                    var pngStream = new MemoryStream(pngBytes, writable: false);
                    disposables.Add(pngStream);
                    dataObject.SetData(PngFormat, false, pngStream);

                    using var bitmap = LoadBitmap(pngBytes);
                    if (bitmap != null)
                    {
                        byte[]? dib = BuildDibV5(bitmap);
                        if (dib != null)
                        {
                            var dibStream = new MemoryStream(dib, writable: false);
                            disposables.Add(dibStream);
                            dataObject.SetData(DibV5Format, false, dibStream);
                        }

                        // CF_BITMAP không có alpha: bẹt nền trắng để app cũ không nhận ảnh nền đen.
                        using var flattened = FlattenOnWhite(bitmap);
                        dataObject.SetData(DataFormats.Bitmap, true, flattened);
                    }

                    // 2. Word/Outlook dán theo HTML Format. Ảnh dư độ phân giải nhưng hiển thị đúng cỡ vector.
                    string base64 = Convert.ToBase64String(pngBytes);
                    string sizeAttributes = displayWidth > 0 && displayHeight > 0
                        ? $" width=\"{displayWidth}\" height=\"{displayHeight}\""
                        : "";
                    string fragment = $"<img src=\"data:image/png;base64,{base64}\"{sizeAttributes}" +
                                      $" alt=\"{System.Net.WebUtility.HtmlEncode(suggestedName)}\">";
                    dataObject.SetText(MarkdownToWordConverter.PackageAsClipboardHtml(fragment), TextDataFormat.Html);

                    // 3. Giữ đường quay về vector: dán vào Figma/Illustrator vẫn ra SVG.
                    if (!string.IsNullOrEmpty(svgMarkup))
                    {
                        var svgStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgMarkup), writable: false);
                        disposables.Add(svgStream);
                        dataObject.SetData(SvgMimeFormat, false, svgStream);
                        dataObject.SetText(svgMarkup, TextDataFormat.UnicodeText);
                    }

                    // 4. File: giữ .svg gốc, hoặc trỏ sang PNG tạm khi người dùng chọn thay hẳn.
                    string[]? files = replaceOriginal
                        ? WriteTempPng(pngBytes, suggestedName)
                        : originalFilePaths;
                    if (files != null && files.Length > 0)
                    {
                        var collection = new System.Collections.Specialized.StringCollection();
                        collection.AddRange(files);
                        dataObject.SetFileDropList(collection);
                    }

                    dataObject.SetData(MarkerFormat, true);

                    Clipboard.SetDataObject(dataObject, true);
                    return true;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(20);
                }
                catch (Exception ex)
                {
                    Program.Log($"[SVG] Lỗi ghi ảnh lên clipboard: {ex.Message}");
                    return false;
                }
                finally
                {
                    // Clipboard.SetDataObject đã sao chép dữ liệu sang OLE nên giải phóng stream là an toàn.
                    foreach (var item in disposables)
                    {
                        try { item.Dispose(); } catch { }
                    }
                }
            }

            return false;
        }

        private static Bitmap? LoadBitmap(byte[] pngBytes)
        {
            try
            {
                using var stream = new MemoryStream(pngBytes, writable: false);
                using var loaded = new Bitmap(stream);
                return loaded.Clone(new Rectangle(0, 0, loaded.Width, loaded.Height), PixelFormat.Format32bppArgb);
            }
            catch (Exception ex)
            {
                Program.Log($"[SVG] Không đọc được PNG vừa render: {ex.Message}");
                return null;
            }
        }

        private static Bitmap FlattenOnWhite(Bitmap source)
        {
            var flattened = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(flattened);
            graphics.Clear(Color.White);
            graphics.DrawImageUnscaled(source, 0, 0);
            return flattened;
        }

        /// <summary>
        /// Dựng CF_DIBV5 (BITMAPV5HEADER + BGRA bottom-up) — định dạng bitmap clipboard duy nhất mang được alpha.
        /// </summary>
        private static byte[]? BuildDibV5(Bitmap bitmap)
        {
            BitmapData? data = null;
            try
            {
                int width = bitmap.Width;
                int height = bitmap.Height;
                int stride = width * 4;
                int imageSize = stride * height;

                data = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

                var buffer = new byte[BitmapV5HeaderSize + imageSize];
                using (var writer = new BinaryWriter(new MemoryStream(buffer)))
                {
                    writer.Write(BitmapV5HeaderSize);
                    writer.Write(width);
                    writer.Write(height); // dương = bottom-up
                    writer.Write((short)1);
                    writer.Write((short)32);
                    writer.Write(BI_BITFIELDS);
                    writer.Write((uint)imageSize);
                    writer.Write(3780); // ~96 DPI
                    writer.Write(3780);
                    writer.Write(0u); // ClrUsed
                    writer.Write(0u); // ClrImportant
                    writer.Write(0x00FF0000u); // RedMask
                    writer.Write(0x0000FF00u); // GreenMask
                    writer.Write(0x000000FFu); // BlueMask
                    writer.Write(0xFF000000u); // AlphaMask
                    writer.Write(LCS_sRGB);
                    writer.Write(new byte[36]); // Endpoints — bỏ qua khi CSType là sRGB
                    writer.Write(0u); // GammaRed
                    writer.Write(0u); // GammaGreen
                    writer.Write(0u); // GammaBlue
                    writer.Write(LCS_GM_IMAGES);
                    writer.Write(0u); // ProfileData
                    writer.Write(0u); // ProfileSize
                    writer.Write(0u); // Reserved
                }

                for (int row = 0; row < height; row++)
                {
                    IntPtr sourceRow = data.Scan0 + (row * data.Stride);
                    int destinationOffset = BitmapV5HeaderSize + ((height - 1 - row) * stride);
                    Marshal.Copy(sourceRow, buffer, destinationOffset, stride);
                }

                return buffer;
            }
            catch (Exception ex)
            {
                Program.Log($"[SVG] Không dựng được DIBv5: {ex.Message}");
                return null;
            }
            finally
            {
                if (data != null)
                {
                    try { bitmap.UnlockBits(data); } catch { }
                }
            }
        }

        private static string[]? WriteTempPng(byte[] pngBytes, string suggestedName)
        {
            try
            {
                string folder = Path.Combine(ResourcePaths.AppDataRoot, "SvgPaste");
                Directory.CreateDirectory(folder);
                PruneTempFolder(folder);

                string safeName = string.Join("_", suggestedName.Split(Path.GetInvalidFileNameChars()));
                if (string.IsNullOrWhiteSpace(safeName))
                {
                    safeName = "hinh-svg";
                }

                string path = Path.Combine(folder, $"{safeName}-{DateTime.Now:HHmmssfff}.png");
                File.WriteAllBytes(path, pngBytes);
                return new[] { path };
            }
            catch (Exception ex)
            {
                Program.Log($"[SVG] Không ghi được PNG tạm: {ex.Message}");
                return null;
            }
        }

        private static void PruneTempFolder(string folder)
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddHours(-24);
                foreach (string file in Directory.GetFiles(folder, "*.png"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch { }
        }
    }
}
