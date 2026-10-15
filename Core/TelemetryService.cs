using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HapiCopi
{
    /// <summary>
    /// Thu thập mẫu chữ viết tay từ người dùng Free gửi về Cloud Server phục vụ đánh giá và huấn luyện.
    /// Hoạt động 100% ngầm (Non-blocking), có hàng đợi Offline an toàn nếu mất mạng, không gây đơ UI.
    /// </summary>
    public static class TelemetryService
    {
        public const string DefaultTelemetryEndpoint = "https://still-sunset-760a.nguyenluonghoangthien5605.workers.dev/api/telemetry/strokes";
        private const int MaxQueueSize = 500;
        private static readonly object _lock = new object();
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static bool _isSyncing = false;
        private static System.Threading.Timer? _syncTimer;

        public static string QueueFilePath => Path.Combine(ResourcePaths.AppDataRoot, "telemetry_queue.json");

        static TelemetryService()
        {
            try
            {
                // Đồng bộ ngầm định kỳ mỗi 60 giây nếu có dữ liệu đệm
                _syncTimer = new System.Threading.Timer(_ => _ = TrySyncQueuedTelemetryAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
            }
            catch
            {
                // Bỏ qua lỗi khởi tạo timer
            }
        }

        public class TelemetryItem
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string MachineId { get; set; } = "";
            public string Label { get; set; } = "";
            public string PredictedLabel { get; set; } = "";
            public int StrokeCount { get; set; } = 1;
            public string StrokesJson { get; set; } = "";
            public string? ImageBase64 { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        /// <summary>
        /// Đưa 1 mẫu nét vẽ vào hàng đợi để gửi về server.
        /// </summary>
        public static void EnqueueSample(string label, string predictedLabel, List<List<PointF>> strokes, string? imageBase64 = null)
        {
            if (string.IsNullOrWhiteSpace(label) || strokes == null || strokes.Count == 0)
                return;

            Task.Run(() =>
            {
                try
                {
                    var serializedStrokes = strokes.Select(s => s.Select(p => new { x = p.X, y = p.Y }).ToList()).ToList();
                    string json = JsonSerializer.Serialize(serializedStrokes);

                    var item = new TelemetryItem
                    {
                        MachineId = LicenseService.MachineId,
                        Label = label.Trim(),
                        PredictedLabel = (predictedLabel ?? "").Trim(),
                        StrokeCount = strokes.Count,
                        StrokesJson = json,
                        ImageBase64 = imageBase64,
                        CreatedAt = DateTime.UtcNow
                    };

                    lock (_lock)
                    {
                        var queue = LoadQueueInternal();
                        queue.Add(item);

                        // Giới hạn hàng đợi tối đa tránh phình đĩa khi offline quá lâu
                        if (queue.Count > MaxQueueSize)
                        {
                            queue = queue.Skip(queue.Count - MaxQueueSize).ToList();
                        }

                        SaveQueueInternal(queue);
                    }

                    // Kích hoạt đồng bộ ngay sau 3 giây (debounce)
                    Task.Delay(3000).ContinueWith(_ => TrySyncQueuedTelemetryAsync());
                }
                catch (Exception ex)
                {
                    Program.Log($"[Telemetry] Lỗi ghi hàng đợi: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Đồng bộ các mẫu nét còn tồn trong hàng đợi lên Cloudflare Worker.
        /// </summary>
        public static async Task TrySyncQueuedTelemetryAsync()
        {
            if (_isSyncing) return;
            if (!NetworkInterface.GetIsNetworkAvailable()) return;

            List<TelemetryItem> itemsToSync;
            lock (_lock)
            {
                var queue = LoadQueueInternal();
                if (queue.Count == 0) return;
                // Mỗi đợt gửi tối đa 10 mẫu để giữ payload nhẹ
                itemsToSync = queue.Take(10).ToList();
            }

            _isSyncing = true;
            try
            {
                var syncedIds = new HashSet<string>();

                foreach (var item in itemsToSync)
                {
                    var payload = new
                    {
                        machineId = item.MachineId,
                        label = item.Label,
                        predictedLabel = item.PredictedLabel,
                        strokeCount = item.StrokeCount,
                        strokesJson = item.StrokesJson,
                        imageBase64 = item.ImageBase64
                    };

                    string jsonBody = JsonSerializer.Serialize(payload);
                    using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    try
                    {
                        var resp = await _httpClient.PostAsync(DefaultTelemetryEndpoint, content);
                        if (resp.IsSuccessStatusCode)
                        {
                            syncedIds.Add(item.Id);
                        }
                    }
                    catch
                    {
                        // Mất mạng tạm thời -> dừng gửi đợt này, để lại trong queue
                        break;
                    }
                }

                if (syncedIds.Count > 0)
                {
                    lock (_lock)
                    {
                        var queue = LoadQueueInternal();
                        queue.RemoveAll(x => syncedIds.Contains(x.Id));
                        SaveQueueInternal(queue);
                    }
                    Program.Log($"[Telemetry] Đã đồng bộ thành công {syncedIds.Count} mẫu nét lên Cloud Server.");
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private static List<TelemetryItem> LoadQueueInternal()
        {
            try
            {
                string path = QueueFilePath;
                if (!File.Exists(path)) return new List<TelemetryItem>();
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<TelemetryItem>>(json) ?? new List<TelemetryItem>();
            }
            catch
            {
                return new List<TelemetryItem>();
            }
        }

        private static void SaveQueueInternal(List<TelemetryItem> queue)
        {
            try
            {
                string path = QueueFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string json = JsonSerializer.Serialize(queue);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Program.Log($"[Telemetry] Không thể lưu file queue: {ex.Message}");
            }
        }
    }
}
