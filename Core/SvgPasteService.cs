using System;
using System.Threading;
using System.Threading.Tasks;

namespace HapiCopi
{
    public sealed class SvgPasteResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
    }

    /// <summary>
    /// Biến SVG trên clipboard thành PNG dán được thẳng vào Word/Docs/Slack, hoàn toàn offline.
    /// Thay cho việc phải tải file lên web converter — vốn là một đường rò dữ liệu thật.
    /// </summary>
    public static class SvgPasteService
    {
        // Tách khỏi dải id của Mermaid/Kroki (dùng số lượng cache) để hai luồng không ghi đè kết quả của nhau.
        private static int _jobId = 1_000_000;

        private const int WebViewWaitMs = 6000;

        public static bool IsEnabled =>
            (AppConfig.Get("SVG_TO_PNG_ENABLED") ?? "true").Trim().ToLowerInvariant() != "false";

        /// <summary>
        /// true = thay hẳn clipboard bằng PNG; false (mặc định) = bổ sung PNG nhưng giữ nguyên file/markup gốc.
        /// </summary>
        public static bool ReplaceOriginal =>
            (AppConfig.Get("SVG_PASTE_MODE") ?? "augment").Trim().ToLowerInvariant() == "replace";

        public static async Task<SvgPasteResult> ConvertAndPublishAsync(SvgPayload payload, string[]? originalFilePaths)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.Markup))
            {
                return new SvgPasteResult { Success = false, Message = "Không có nội dung SVG." };
            }

            if (!WebViewManager.IsInitialized)
            {
                await Task.WhenAny(WebViewManager.InitializationTask, Task.Delay(WebViewWaitMs));
            }

            if (!WebViewManager.IsInitialized)
            {
                Program.Log("[SVG] WebView2 chưa sẵn sàng, không rasterize được SVG.");
                return new SvgPasteResult { Success = false, Message = "WebView2 chưa sẵn sàng." };
            }

            int id = Interlocked.Increment(ref _jobId);
            SvgRasterResult raster = await WebViewManager.RenderSvgDetailedAsync(payload.Markup, id, ReadOptions());

            if (!raster.Success)
            {
                Program.Log($"[SVG] Rasterize thất bại ({payload.SourceLabel}): {raster.Error}");
                return new SvgPasteResult { Success = false, Message = "Không render được SVG này." };
            }

            byte[]? png = DecodeDataUri(raster.DataUri);
            if (png == null || png.Length == 0)
            {
                return new SvgPasteResult { Success = false, Message = "Ảnh PNG rỗng." };
            }

            (int pixelWidth, int pixelHeight) = ReadPngSize(png);
            (int displayWidth, int displayHeight) = FitToPage(
                raster.IntrinsicWidth > 0 ? raster.IntrinsicWidth : pixelWidth,
                raster.IntrinsicHeight > 0 ? raster.IntrinsicHeight : pixelHeight);

            bool written = ClipboardImageWriter.SetPngWithSvgFallback(
                png,
                payload.Markup,
                originalFilePaths,
                ReplaceOriginal,
                payload.SuggestedName,
                displayWidth,
                displayHeight);

            if (!written)
            {
                return new SvgPasteResult { Success = false, Message = "Không ghi được clipboard." };
            }

            Program.Log($"[SVG] Đã chuyển {payload.SourceLabel} thành PNG {pixelWidth}x{pixelHeight} " +
                        $"(hiển thị {displayWidth}x{displayHeight}, {png.Length} bytes).");

            return new SvgPasteResult
            {
                Success = true,
                Width = displayWidth,
                Height = displayHeight,
                Message = $"SVG đã thành ảnh {displayWidth}×{displayHeight}. Ctrl+V vào Word/Docs là ra ảnh."
            };
        }

        /// <summary>
        /// Giữ đúng cỡ vector khi dán, nhưng không để bản vẽ khổ lớn tràn ra ngoài lề trang A4.
        /// </summary>
        private static (int Width, int Height) FitToPage(int width, int height)
        {
            const int MaxPageWidth = 620; // ~16.4cm, vừa khổ A4 với lề mặc định của Word

            if (width <= 0 || height <= 0)
            {
                return (width, height);
            }

            if (width <= MaxPageWidth)
            {
                return (width, height);
            }

            double ratio = (double)MaxPageWidth / width;
            return (MaxPageWidth, Math.Max(1, (int)Math.Round(height * ratio)));
        }

        private static SvgRasterOptions ReadOptions()
        {
            string background = (AppConfig.Get("SVG_PNG_BACKGROUND") ?? "transparent").Trim().ToLowerInvariant();

            int minWidth = 1200;
            if (int.TryParse(AppConfig.Get("SVG_PNG_MIN_WIDTH"), out int configured) && configured > 0)
            {
                minWidth = Math.Min(configured, 8000);
            }

            return new SvgRasterOptions
            {
                Background = background is "transparent" or "none" or "" ? null : background,
                MinWidth = minWidth,
                MaxScale = 2,
                TimeoutMs = 15000
            };
        }

        private static byte[]? DecodeDataUri(string dataUri)
        {
            try
            {
                int comma = dataUri.IndexOf(',');
                if (comma < 0)
                {
                    return null;
                }
                return Convert.FromBase64String(dataUri.Substring(comma + 1));
            }
            catch (Exception ex)
            {
                Program.Log($"[SVG] Data URI hỏng: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Đọc kích thước từ chunk IHDR, rẻ hơn dựng cả Bitmap chỉ để lấy hai con số.
        /// </summary>
        private static (int Width, int Height) ReadPngSize(byte[] png)
        {
            if (png.Length < 24)
            {
                return (0, 0);
            }

            int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return (width, height);
        }
    }
}
