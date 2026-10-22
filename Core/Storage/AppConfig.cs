using System;
using System.Collections.Generic;
using System.IO;

namespace HapiCopi
{
    /// <summary>
    /// Quản lý cấu hình tập trung cho HapiCopi (API key, model, provider).
    /// Ưu tiên đọc: Biến môi trường > File config người dùng (%APPDATA%\HapiCopi\config.env) > File .env trong dự án.
    /// Cho phép ghi cấu hình của người dùng để dùng được khi đóng gói thành bản build phân phối.
    /// </summary>
    public static class AppConfig
    {
        public static string ConfigDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HapiCopi");

        public static string ConfigPath => Path.Combine(ConfigDirectory, "config.env");

        private static readonly Dictionary<string, string?> _cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Lấy giá trị cấu hình theo thứ tự ưu tiên: ENV > config người dùng > .env dự án (có cache in-memory).
        /// </summary>
        public static string? Get(string key)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var cachedVal))
                {
                    return cachedVal;
                }
            }

            var fromEnv = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                string res = fromEnv.Trim();
                lock (_cacheLock) { _cache[key] = res; }
                return res;
            }

            var fromUserConfig = ReadKeyFromFile(ConfigPath, key);
            if (!string.IsNullOrWhiteSpace(fromUserConfig))
            {
                lock (_cacheLock) { _cache[key] = fromUserConfig; }
                return fromUserConfig;
            }

            // Fallback: đọc từ file .env trong thư mục ứng dụng
            var dotEnvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            var fromDotEnv = ReadKeyFromFile(dotEnvPath, key);
            if (!string.IsNullOrWhiteSpace(fromDotEnv))
            {
                lock (_cacheLock) { _cache[key] = fromDotEnv; }
                return fromDotEnv;
            }

            // Fallback dev: tìm file .env ở thư mục gốc dự án
            try
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                var root = dir.Parent?.Parent?.Parent;
                if (root != null)
                {
                    var devDotEnv = Path.Combine(root.FullName, ".env");
                    var fromDev = ReadKeyFromFile(devDotEnv, key);
                    if (!string.IsNullOrWhiteSpace(fromDev))
                    {
                        lock (_cacheLock) { _cache[key] = fromDev; }
                        return fromDev;
                    }
                }
            }
            catch { }

            lock (_cacheLock) { _cache[key] = null; }
            return null;
        }

        /// <summary>
        /// Ghi (thêm mới hoặc cập nhật) một giá trị cấu hình vào file config người dùng và cập nhật cache.
        /// </summary>
        public static void Set(string key, string? value)
        {
            try
            {
                lock (_cacheLock)
                {
                    _cache[key] = value?.Trim();
                }

                Directory.CreateDirectory(ConfigDirectory);

                var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var order = new List<string>();

                if (File.Exists(ConfigPath))
                {
                    foreach (var line in File.ReadAllLines(ConfigPath))
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        {
                            continue;
                        }
                        int idx = trimmed.IndexOf('=');
                        if (idx <= 0)
                        {
                            continue;
                        }
                        string existingKey = trimmed.Substring(0, idx).Trim();
                        string existingVal = trimmed.Substring(idx + 1).Trim();
                        if (!entries.ContainsKey(existingKey))
                        {
                            order.Add(existingKey);
                        }
                        entries[existingKey] = existingVal;
                    }
                }

                if (!entries.ContainsKey(key))
                {
                    order.Add(key);
                }
                entries[key] = value?.Trim() ?? "";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# Cấu hình HapiCopi (tự động sinh bởi cửa sổ Cài đặt). Không chia sẻ file này vì chứa API key.");
                foreach (var k in order)
                {
                    sb.AppendLine($"{k}={entries[k]}");
                }

                File.WriteAllText(ConfigPath, sb.ToString());
            }
            catch (Exception ex)
            {
                Program.Log($"[AppConfig] Lỗi khi ghi cấu hình: {ex.Message}");
                throw;
            }
        }

        private static string? ReadKeyFromFile(string path, string key)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                foreach (var line in File.ReadAllLines(path))
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    {
                        continue;
                    }
                    if (trimmed.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = trimmed.Substring((key + "=").Length).Trim();
                        if ((val.StartsWith("\"") && val.EndsWith("\"")) ||
                            (val.StartsWith("'") && val.EndsWith("'")))
                        {
                            val = val.Length >= 2 ? val.Substring(1, val.Length - 2) : val;
                        }
                        return string.IsNullOrEmpty(val) ? null : val;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[AppConfig] Lỗi khi đọc cấu hình: {ex.Message}");
            }

            return null;
        }
    }
}
