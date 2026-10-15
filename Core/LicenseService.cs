using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace HapiCopi
{
    /// <summary>
    /// Cấu hình Thông tin Thanh toán VietQR & Mức giá triển khai của HapiCopi
    /// </summary>
    public static class PaymentConfig
    {
        public static string BankName { get; set; } = "Vietinbank";          // Tên ngân hàng VietQR (Vietinbank, Vietcombank, MBBank, ...)
        public static string BankAccount { get; set; } = "101888544117";     // Số tài khoản người nhận
        public static string AccountHolder { get; set; } = "NGUYEN LUONG HOANG THIEN"; // Tên chủ tài khoản
        public static int PriceProLifetime { get; set; } = 29999;            // Mức giá Bản Pro Trọn Đời (VNĐ)
        public static int PriceCloudMonthly { get; set; } = 9999;            // Mức giá Gói Cloud 1 tháng (VNĐ)
    }

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
        public int BonusQuotaToday { get; set; } = 0; // Lượt thưởng nhận được khi người dùng đóng góp dán nhãn nét viết tay
    }

    /// <summary>
    /// Quản lý Hạn mức & Bản quyền minh bạch của HapiCopi:
    /// - Bản Free: 50 lượt nhận diện Offline / ngày (reset lúc 00:00).
    /// - Bản Pro Mua đứt (19k): Không giới hạn lượt offline vĩnh viễn, tắt sạch quảng cáo.
    /// - Gói Cloud (9k/tháng): 1.000 lượt Cloud AI Gemini Flash siêu tốc.
    /// - Bảo vệ Machine Binding: License Key gắn chặt với MachineGuid của máy tính.
    /// </summary>
    public static class LicenseService
    {
        public const int MaxDailyFreeQuota = 50;
        public const int MaxMonthlyCloudQuota = 1000;
        private const string SecretSalt = "hapicopi_master_salt_exe_2026";

        private static readonly object _lock = new object();
        private static LicenseData _data = new LicenseData();
        private static bool _isLoaded = false;

        /// <summary>
        /// Đường dẫn lưu trữ tùy chọn (dùng riêng cho Unit Test để không ghi đè dữ liệu thật của người dùng)
        /// </summary>
        public static string? CustomStoragePath { get; set; } = null;

        private static string LicenseFilePath =>
            CustomStoragePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HapiCopi", "license.dat");

        private static byte[] Entropy => Encoding.UTF8.GetBytes("HapiCopi_DPAPI_License_2026");

        private static string? _cachedMachineId = null;

        static LicenseService()
        {
            Load();
        }

        /// <summary>
        /// Mã định danh máy tính phần cứng duy nhất (8 ký tự hex), dùng để gắn bản quyền
        /// </summary>
        public static string MachineId
        {
            get
            {
                if (_cachedMachineId == null)
                {
                    _cachedMachineId = ResolveMachineId();
                }
                return _cachedMachineId;
            }
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

        public static int EffectiveDailyFreeQuota
        {
            get
            {
                EnsureLoadedAndResetDaily();
                return MaxDailyFreeQuota + _data.BonusQuotaToday;
            }
        }

        public static int RemainingDailyFreeQuota
        {
            get
            {
                EnsureLoadedAndResetDaily();
                if (IsProLifetime) return 999999;
                return Math.Max(0, EffectiveDailyFreeQuota - _data.DailyUsed);
            }
        }

        /// <summary>
        /// Cộng thêm lượt nhận diện AI miễn phí trong ngày (khi người dùng đóng góp dán nhãn nét chữ)
        /// </summary>
        public static void AddFreeBonusQuota(int count)
        {
            if (count <= 0) return;
            lock (_lock)
            {
                EnsureLoadedAndResetDaily();
                _data.BonusQuotaToday += count;
                SaveInternal();
                Program.Log($"[LicenseService] Đã cộng thêm +{count} lượt AI thưởng. Hạn mức hiện tại: {RemainingDailyFreeQuota}/{EffectiveDailyFreeQuota}");
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

                    reason = "Bạn chưa kích hoạt gói Cloud AI (9.000đ/tháng) hoặc đã sử dụng hết 1.000 lượt trong tháng.";
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

                int effectiveMax = EffectiveDailyFreeQuota;
                if (_data.DailyUsed >= effectiveMax)
                {
                    reason = $"Bạn đã dùng hết {effectiveMax} lượt nhận diện miễn phí hôm nay. Bạn có thể đóng góp nét viết tay để nhận thêm lượt (+5 lượt/mẫu) hoặc chờ hồi phục lúc 00:00.";
                    remaining = 0;
                    maxQuota = effectiveMax;
                    return false;
                }

                reason = "";
                remaining = effectiveMax - _data.DailyUsed;
                maxQuota = effectiveMax;
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
        /// Xác thực và kích hoạt License Key theo chuẩn gắn mã phần cứng MachineId:
        /// - Pro Lifetime: HAPI-PRO-{MachineId}-{Signature}
        /// - Cloud 30 Ngày: HAPI-CLD-{MachineId}-{Signature}
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
                return (false, "Mã License không đúng định dạng HapiCopi (VD: HAPI-PRO-XXXXXXXX-YYYYYYYY).");
            }

            string type = parts[1]; // PRO hoặc CLD
            string targetMachineId = parts[2];
            string signature = parts[3];

            if (targetMachineId.Length != 8 || signature.Length != 8)
            {
                return (false, "Mã License không hợp lệ (Độ dài mã máy hoặc chữ ký không đúng).");
            }

            // Kiểm tra xem mã key có khớp với máy tính hiện tại không
            if (!targetMachineId.Equals(MachineId, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Mã License này được đăng ký cho máy '{targetMachineId}', không khớp với mã máy hiện tại của bạn ('{MachineId}').");
            }

            // Tính chữ ký kiểm tra
            string expectedSig = ComputeSignature(type, targetMachineId);
            if (!signature.Equals(expectedSig, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Chữ ký License không hợp lệ hoặc đã bị can thiệp.");
            }

            lock (_lock)
            {
                EnsureLoadedAndResetDaily();

                if (type == "PRO")
                {
                    _data.IsProLifetime = true;
                    _data.ActiveLicenseKey = key;
                    SaveInternal();
                    Program.Log($"[LicenseService] Đã kích hoạt thành công Pro Lifetime cho máy {MachineId}: {key}");
                    return (true, "Chúc mừng! Đã kích hoạt thành công Bản Pro Trọn Đời (Vô hạn lượt Offline & Tắt quảng cáo).");
                }
                else if (type == "CLD")
                {
                    _data.CloudQuotaRemaining = MaxMonthlyCloudQuota;
                    _data.CloudExpiryDate = DateTime.Today.AddDays(30);
                    _data.ActiveLicenseKey = key;
                    SaveInternal();
                    Program.Log($"[LicenseService] Đã kích hoạt thành công Gói Cloud 1.000 lượt (30 ngày) cho máy {MachineId}: {key}");
                    return (true, "Chúc mừng! Đã kích hoạt thành công Gói Cloud AI: 1.000 lượt nhận diện siêu tốc trong 30 ngày.");
                }
                else
                {
                    return (false, "Loại bản quyền không được hỗ trợ.");
                }
            }
        }

        /// <summary>
        /// Sinh License Key chuẩn gắn theo MachineId của máy đích (dùng cho công cụ cấp bản quyền Admin / Webhook SePay)
        /// </summary>
        public static string GenerateKey(string type, string? targetMachineId = null)
        {
            string t = type.ToUpperInvariant();
            if (t != "PRO" && t != "CLD") t = "PRO";

            string mId = (targetMachineId ?? MachineId).Trim().ToUpperInvariant();
            if (mId.Length != 8)
            {
                mId = ComputeShortHash(mId);
            }

            string sig = ComputeSignature(t, mId);
            return $"HAPI-{t}-{mId}-{sig}";
        }

        private static string ComputeSignature(string type, string machineId)
        {
            using var sha = SHA256.Create();
            byte[] input = Encoding.UTF8.GetBytes($"{type}:{machineId}:{SecretSalt}");
            byte[] hash = sha.ComputeHash(input);
            return Convert.ToHexString(hash).Substring(0, 8).ToUpperInvariant();
        }

        private static string ComputeShortHash(string input)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).Substring(0, 8).ToUpperInvariant();
        }

        private static string ResolveMachineId()
        {
            try
            {
                // 1. Đọc MachineGuid từ Windows Registry 64-bit view
                using var baseKey = RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
                using var subKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                if (subKey?.GetValue("MachineGuid") is string guid && !string.IsNullOrWhiteSpace(guid))
                {
                    return ComputeShortHash(guid.Trim());
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[LicenseService] Không đọc được MachineGuid từ Registry ({ex.Message}), dùng fallback.");
            }

            // Fallback nếu không có quyền đọc Registry
            string fallback = $"{Environment.MachineName}_{Environment.UserName}";
            return ComputeShortHash(fallback);
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
                _data.BonusQuotaToday = 0;
                SaveInternal();
                Program.Log($"[LicenseService] Đã tự động hồi phục 50 lượt miễn phí cho ngày mới ({todayStr}).");
            }
        }

        public static void Reload()
        {
            lock (_lock)
            {
                _isLoaded = false;
                LoadInternal();
            }
        }

        public static void ResetToFreeState()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(LicenseFilePath))
                    {
                        File.Delete(LicenseFilePath);
                    }
                }
                catch { }

                _data = new LicenseData();
                _isLoaded = true;
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
