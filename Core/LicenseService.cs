using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HapiCopi
{
    /// <summary>
    /// Dữ liệu bản quyền và hạn mức sử dụng HapiCopi
    /// </summary>
    public class LicenseData
    {
        public bool IsProLifetime { get; set; } = false;
        public string? ActiveLicenseKey { get; set; } = null;
        public DateTime? CloudExpiryDate { get; set; } = null;
        public int CloudQuotaRemaining { get; set; } = 0;
        public string DailyDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
        public int DailyUsed { get; set; } = 0;
    }

    /// <summary>
    /// Quản lý Hạn mức & Bản quyền minh bạch của HapiCopi:
    /// - Bản Free: 50 lượt nhận diện Offline / ngày (reset lúc 00:00).
    /// - Bản Pro Mua đứt (19k): Không giới hạn lượt offline vĩnh viễn, tắt sạch quảng cáo.
    /// - Gói Cloud (9k/tháng): 1.000 lượt Cloud AI Gemini Flash siêu tốc.
    /// </summary>
    public static class LicenseService
    {
        public const int MaxDailyFreeQuota = 50;
        public const int MaxMonthlyCloudQuota = 1000;
        private const string SecretSalt = "hapicopi_master_salt_exe_2026";

        private static readonly object _lock = new object();
        private static LicenseData _data = new LicenseData();
        private static bool _isLoaded = false;

        private static string LicenseFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HapiCopi", "license.dat");

        private static byte[] Entropy => Encoding.UTF8.GetBytes("HapiCopi_DPAPI_License_2026");

        static LicenseService()
        {
            Load();
        }

        public static bool IsProLifetime
        {
            get
            {
                EnsureLoadedAndResetDaily();
                return _data.IsProLifetime;
            }
        }

        public static bool HasActiveCloud
        {
            get
            {
                EnsureLoadedAndResetDaily();
                return _data.CloudExpiryDate.HasValue &&
                       _data.CloudExpiryDate.Value.Date >= DateTime.Today &&
                       _data.CloudQuotaRemaining > 0;
            }
        }

        public static bool IsPro => IsProLifetime || HasActiveCloud;

        public static int CloudQuotaRemaining
        {
            get
            {
                EnsureLoadedAndResetDaily();
                return _data.CloudQuotaRemaining;
            }
        }

        public static DateTime? CloudExpiryDate
        {
            get
            {
                EnsureLoadedAndResetDaily();
                return _data.CloudExpiryDate;
            }
        }

        public static int DailyUsedToday
        {
            get
            {
                EnsureLoadedAndResetDaily();
                return _data.DailyUsed;
            }
        }

        public static int RemainingDailyFreeQuota
        {
            get
            {
                EnsureLoadedAndResetDaily();
                if (IsPro) return 999999;
                return Math.Max(0, MaxDailyFreeQuota - _data.DailyUsed);
            }
        }

        /// <summary>
        /// Kiểm tra xem có được phép nhận diện toán hay không
        /// </summary>
        public static bool CanRecognize(bool wantsCloud, out string reason, out int remaining, out int maxQuota)
        {
            lock (_lock)
            {
                EnsureLoadedAndResetDaily();

                if (wantsCloud)
                {
                    if (HasActiveCloud)
                    {
                        reason = "";
                        remaining = _data.CloudQuotaRemaining;
                        maxQuota = MaxMonthlyCloudQuota;
                        return true;
                    }

                    reason = "Bạn chưa đăng ký gói Cloud (9.000đ/tháng) hoặc đã sử dụng hết 1.000 lượt trong tháng.";
                    remaining = 0;
                    maxQuota = MaxMonthlyCloudQuota;
                    return false;
                }

                // Chế độ Cục bộ (Offline)
                if (IsProLifetime)
                {
                    reason = "";
                    remaining = 999999;
                    maxQuota = 999999;
                    return true;
                }

                if (_data.DailyUsed >= MaxDailyFreeQuota)
                {
                    reason = "Bạn đã dùng hết 50 lượt nhận diện miễn phí hôm nay. Hạn mức sẽ tự hồi phục vào 00:00 ngày mai.";
                    remaining = 0;
                    maxQuota = MaxDailyFreeQuota;
                    return false;
                }

                reason = "";
                remaining = MaxDailyFreeQuota - _data.DailyUsed;
                maxQuota = MaxDailyFreeQuota;
                return true;
            }
        }

        /// <summary>
        /// Ghi nhận 1 lượt sử dụng nhận diện toán
        /// </summary>
        public static void ConsumeRecognition(bool isCloud)
        {
            lock (_lock)
            {
                EnsureLoadedAndResetDaily();

                if (isCloud)
                {
                    if (_data.CloudQuotaRemaining > 0)
                    {
                        _data.CloudQuotaRemaining--;
                    }
                }
                else
                {
                    if (!_data.IsProLifetime)
                    {
                        _data.DailyUsed++;
                    }
                }

                SaveInternal();
            }
        }

        /// <summary>
        /// Xác thực và kích hoạt License Key
        /// Hỗ trợ 2 loại key:
        /// - Pro Lifetime: HAPI-PRO-XXXXXXXX-YYYYYYYY
        /// - Cloud 30 Ngày (1000 lượt): HAPI-CLD-XXXXXXXX-YYYYYYYY
        /// </summary>
        public static (bool success, string message) ActivateLicenseKey(string rawKey)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                return (false, "Vui lòng nhập License Key.");
            }

            string key = rawKey.Trim().ToUpperInvariant();
            var parts = key.Split('-');
            if (parts.Length != 4 || parts[0] != "HAPI")
            {
                return (false, "Mã License không đúng định dạng HapiCopi.");
            }

            string type = parts[1]; // PRO hoặc CLD
            string seed = parts[2];
            string signature = parts[3];

            if (seed.Length != 8 || signature.Length != 8)
            {
                return (false, "Mã License không hợp lệ.");
            }

            // Tính chữ ký kiểm tra
            string expectedSig = ComputeSignature(type, seed);
            if (!signature.Equals(expectedSig, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Mã License không hợp lệ hoặc đã bị chỉnh sửa.");
            }

            lock (_lock)
            {
                EnsureLoadedAndResetDaily();

                if (type == "PRO")
                {
                    _data.IsProLifetime = true;
                    _data.ActiveLicenseKey = key;
                    SaveInternal();
                    Program.Log($"[LicenseService] Đã kích hoạt thành công Pro Lifetime: {key}");
                    return (true, "Chúc mừng! Đã kích hoạt thành công Bản Pro Trọn Đời (Vô hạn lượt Offline & Tắt quảng cáo).");
                }
                else if (type == "CLD")
                {
                    _data.CloudQuotaRemaining = MaxMonthlyCloudQuota;
                    _data.CloudExpiryDate = DateTime.Today.AddDays(30);
                    _data.ActiveLicenseKey = key;
                    SaveInternal();
                    Program.Log($"[LicenseService] Đã kích hoạt thành công Gói Cloud 1.000 lượt (30 ngày): {key}");
                    return (true, "Chúc mừng! Đã kích hoạt thành công Gói Cloud AI: 1.000 lượt nhận diện siêu tốc trong 30 ngày.");
                }
                else
                {
                    return (false, "Loại bản quyền không được hỗ trợ.");
                }
            }
        }

        /// <summary>
        /// Sinh License Key chuẩn thuật toán (dùng cho công cụ cấp phát bản quyền của Admin)
        /// </summary>
        public static string GenerateKey(string type, string? optionalSeed = null)
        {
            string t = type.ToUpperInvariant();
            if (t != "PRO" && t != "CLD") t = "PRO";

            string seed = optionalSeed ?? Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            string sig = ComputeSignature(t, seed);
            return $"HAPI-{t}-{seed}-{sig}";
        }

        private static string ComputeSignature(string type, string seed)
        {
            using var sha = SHA256.Create();
            byte[] input = Encoding.UTF8.GetBytes($"{type}:{seed}:{SecretSalt}");
            byte[] hash = sha.ComputeHash(input);
            return Convert.ToHexString(hash).Substring(0, 8).ToUpperInvariant();
        }

        private static void EnsureLoadedAndResetDaily()
        {
            if (!_isLoaded)
            {
                LoadInternal();
            }

            string todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            if (_data.DailyDate != todayStr)
            {
                _data.DailyDate = todayStr;
                _data.DailyUsed = 0;
                SaveInternal();
                Program.Log($"[LicenseService] Đã tự động hồi phục 50 lượt miễn phí cho ngày mới ({todayStr}).");
            }
        }

        public static void Load()
        {
            lock (_lock)
            {
                LoadInternal();
            }
        }

        private static void LoadInternal()
        {
            try
            {
                if (File.Exists(LicenseFilePath))
                {
                    byte[] encrypted = File.ReadAllBytes(LicenseFilePath);
                    byte[] decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                    string json = Encoding.UTF8.GetString(decrypted);
                    _data = JsonSerializer.Deserialize<LicenseData>(json) ?? new LicenseData();
                    _isLoaded = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[LicenseService] Không thể nạp license.dat: {ex.Message}. Khởi tạo mặc định.");
            }

            _data = new LicenseData();
            _isLoaded = true;
        }

        private static void SaveInternal()
        {
            try
            {
                string dir = Path.GetDirectoryName(LicenseFilePath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(_data);
                byte[] plaintext = Encoding.UTF8.GetBytes(json);
                byte[] encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(LicenseFilePath, encrypted);
            }
            catch (Exception ex)
            {
                Program.Log($"[LicenseService] Lỗi khi lưu license.dat: {ex.Message}");
            }
        }
    }
}
