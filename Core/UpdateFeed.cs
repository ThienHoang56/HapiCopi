using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HapiCopi
{
    /// <summary>
    /// Đọc danh sách bản phát hành và tải bộ cài.
    ///
    /// Nguồn cập nhật chỉ là một địa chỉ tới file versions.json, nên dùng được cả thư mục trên máy,
    /// thư mục chia sẻ trong mạng LAN, lẫn URL https — đổi nguồn không phải sửa code.
    /// </summary>
    public static class UpdateFeed
    {
        private const string ManifestFileName = "versions.json";
        private const long MaxManifestBytes = 1024 * 1024;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HapiCopi-Updater");
            return client;
        }

        public static bool IsHttp(string location) =>
            location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Chuẩn hóa nguồn thành đường dẫn đầy đủ tới versions.json (người dùng có thể chỉ khai thư mục).
        /// </summary>
        public static string ResolveManifestLocation(string feed)
        {
            feed = (feed ?? "").Trim();
            if (feed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return feed;
            }

            return IsHttp(feed)
                ? $"{feed.TrimEnd('/')}/{ManifestFileName}"
                : Path.Combine(feed, ManifestFileName);
        }

        public static string ResolveAssetLocation(string feed, string fileName)
        {
            string manifest = ResolveManifestLocation(feed);
            if (IsHttp(manifest))
            {
                int lastSlash = manifest.LastIndexOf('/');
                return $"{manifest.Substring(0, lastSlash)}/{Uri.EscapeDataString(fileName)}";
            }

            string? folder = Path.GetDirectoryName(manifest);
            return Path.Combine(folder ?? "", fileName);
        }

        public static async Task<UpdateManifest?> ReadManifestAsync(string feed, CancellationToken token = default)
        {
            string location = ResolveManifestLocation(feed);

            try
            {
                string json;
                if (IsHttp(location))
                {
                    using var response = await Http.GetAsync(location, token);
                    response.EnsureSuccessStatusCode();
                    json = await response.Content.ReadAsStringAsync(token);
                }
                else
                {
                    if (!System.IO.File.Exists(location))
                    {
                        Program.Log($"[Update] Chưa có {location}.");
                        return null;
                    }

                    var info = new FileInfo(location);
                    if (info.Length > MaxManifestBytes)
                    {
                        Program.Log($"[Update] versions.json quá lớn ({info.Length} bytes).");
                        return null;
                    }

                    json = await System.IO.File.ReadAllTextAsync(location, token);
                }

                var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
                if (manifest == null || manifest.Releases.Count == 0)
                {
                    Program.Log("[Update] versions.json không có bản phát hành nào.");
                    return null;
                }

                return manifest;
            }
            catch (Exception ex)
            {
                Program.Log($"[Update] Không đọc được danh sách phiên bản từ {location}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Tải bộ cài về <paramref name="destination"/>. Ghi ra file .part rồi mới đổi tên,
        /// để lần chạy sau không nhầm một file tải dở là bản cài hoàn chỉnh.
        /// </summary>
        public static async Task<bool> DownloadAsync(
            string feed,
            ReleaseInfo release,
            string destination,
            IProgress<int>? progress,
            CancellationToken token = default)
        {
            string source = ResolveAssetLocation(feed, release.File);
            string partial = destination + ".part";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                if (System.IO.File.Exists(partial))
                {
                    System.IO.File.Delete(partial);
                }

                if (IsHttp(source))
                {
                    await DownloadHttpAsync(source, partial, release.Size, progress, token);
                }
                else
                {
                    await CopyLocalAsync(source, partial, progress, token);
                }

                if (!await VerifyAsync(partial, release, token))
                {
                    System.IO.File.Delete(partial);
                    return false;
                }

                if (System.IO.File.Exists(destination))
                {
                    System.IO.File.Delete(destination);
                }
                System.IO.File.Move(partial, destination);

                progress?.Report(100);
                return true;
            }
            catch (Exception ex)
            {
                Program.Log($"[Update] Tải {release.Version} thất bại: {ex.Message}");
                try { if (System.IO.File.Exists(partial)) System.IO.File.Delete(partial); } catch { }
                return false;
            }
        }

        private static async Task DownloadHttpAsync(
            string url, string destination, long expectedSize, IProgress<int>? progress, CancellationToken token)
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? expectedSize;
            using var input = await response.Content.ReadAsStreamAsync(token);
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            await PumpAsync(input, output, total, progress, token);
        }

        private static async Task CopyLocalAsync(
            string source, string destination, IProgress<int>? progress, CancellationToken token)
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            await PumpAsync(input, output, input.Length, progress, token);
        }

        private static async Task PumpAsync(
            Stream input, Stream output, long total, IProgress<int>? progress, CancellationToken token)
        {
            var buffer = new byte[81920];
            long copied = 0;
            int lastPercent = -1;
            int read;

            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                copied += read;

                if (total > 0)
                {
                    int percent = (int)Math.Min(99, copied * 100 / total);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(percent);
                    }
                }
            }
        }

        /// <summary>
        /// Đối chiếu kích thước và SHA256. Bộ cài là file thực thi sẽ chạy với quyền của người dùng,
        /// nên chỉ chấp nhận khi khớp đúng những gì manifest công bố.
        /// </summary>
        public static async Task<bool> VerifyAsync(string path, ReleaseInfo release, CancellationToken token = default)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return false;
                }

                if (release.Size > 0 && info.Length != release.Size)
                {
                    Program.Log($"[Update] Kích thước lệch: {info.Length} ≠ {release.Size} ({release.Version}).");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(release.Sha256))
                {
                    Program.Log($"[Update] Manifest không khai SHA256 cho {release.Version}, bỏ qua bước đối chiếu mã băm.");
                    return true;
                }

                string actual = await ComputeSha256Async(path, token);
                if (!actual.Equals(release.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Program.Log($"[Update] SHA256 không khớp cho {release.Version}. Đã bỏ file tải về.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Program.Log($"[Update] Lỗi kiểm tra file cài: {ex.Message}");
                return false;
            }
        }

        public static async Task<string> ComputeSha256Async(string path, CancellationToken token = default)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            byte[] hash = await SHA256.HashDataAsync(stream, token);
            return Convert.ToHexString(hash);
        }
    }
}
