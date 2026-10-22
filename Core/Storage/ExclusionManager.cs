using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HapiCopi
{
    /// <summary>
    /// Quản lý danh sách các chuỗi được người dùng chủ động bỏ qua bảo mật (Exclusions).
    /// Sử dụng cơ chế băm một chiều SHA-256 và mã hóa bằng Windows DPAPI để bảo mật tối đa.
    /// </summary>
    public static class ExclusionManager
    {
        private static string ConfigDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HapiCopi");

        private static string ExclusionsPath => Path.Combine(ConfigDirectory, "exclusions.dat");

        private static readonly HashSet<string> ExcludedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object LockObj = new object();

        // Muối bảo mật (entropy) để tăng cường bảo vệ DPAPI
        private static byte[] GetOptionalEntropy()
        {
            return new byte[] { 0x48, 0x61, 0x70, 0x69, 0x43, 0x6F, 0x70, 0x69, 0x5F, 0x45, 0x78, 0x63, 0x6C, 0x75, 0x73, 0x69, 0x6F, 0x6E, 0x73, 0x5F, 0x45 };
        }

        static ExclusionManager()
        {
            LoadExclusions();
        }

        /// <summary>
        /// Tính toán mã băm SHA-256 của chuỗi để tránh lưu trữ văn bản rõ nhạy cảm trên đĩa.
        /// </summary>
        private static string ComputeSha256Hash(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Nạp các mã băm ngoại lệ từ tệp tin mã hóa DPAPI cục bộ.
        /// </summary>
        public static void LoadExclusions()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);

                if (File.Exists(ExclusionsPath))
                {
                    byte[] encryptedBytes = File.ReadAllBytes(ExclusionsPath);
                    byte[] entropy = GetOptionalEntropy();
                    byte[] plaintextBytes = ProtectedData.Unprotect(encryptedBytes, entropy, DataProtectionScope.CurrentUser);
                    
                    string content = Encoding.UTF8.GetString(plaintextBytes);
                    var hashes = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    
                    lock (LockObj)
                    {
                        ExcludedHashes.Clear();
                        foreach (var hash in hashes)
                        {
                            string trimmed = hash.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                ExcludedHashes.Add(trimmed);
                            }
                        }
                    }
                    Program.Log($"[ExclusionManager] Đã nạp thành công {ExcludedHashes.Count} mã ngoại lệ đã mã hóa từ đĩa.");
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[ExclusionManager LỖI] Không thể nạp danh sách ngoại lệ: {ex.Message}");
            }
        }

        /// <summary>
        /// Lưu danh sách mã băm ngoại lệ xuống tệp tin mã hóa DPAPI.
        /// </summary>
        public static void SaveExclusions()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);

                string content;
                lock (LockObj)
                {
                    content = string.Join(Environment.NewLine, ExcludedHashes);
                }

                byte[] plaintextBytes = Encoding.UTF8.GetBytes(content);
                byte[] entropy = GetOptionalEntropy();
                byte[] encryptedBytes = ProtectedData.Protect(plaintextBytes, entropy, DataProtectionScope.CurrentUser);
                
                File.WriteAllBytes(ExclusionsPath, encryptedBytes);
            }
            catch (Exception ex)
            {
                Program.Log($"[ExclusionManager LỖI] Không thể lưu danh sách ngoại lệ: {ex.Message}");
            }
        }

        /// <summary>
        /// Thêm một chuỗi gốc vào danh sách ngoại lệ bảo mật (sẽ được băm SHA-256 trước khi lưu).
        /// </summary>
        public static void AddExclusion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            string hash = ComputeSha256Hash(text);
            lock (LockObj)
            {
                if (ExcludedHashes.Add(hash))
                {
                    SaveExclusions();
                    Program.Log($"[ExclusionManager] Đã thêm ngoại lệ thành công cho chuỗi (SHA256: {hash.Substring(0, 8)}...)");
                }
            }
        }

        /// <summary>
        /// Kiểm tra xem chuỗi có khớp với bất kỳ mã băm ngoại lệ nào đã lưu không.
        /// </summary>
        public static bool IsExcluded(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string hash = ComputeSha256Hash(text);
            lock (LockObj)
            {
                return ExcludedHashes.Contains(hash);
            }
        }
    }
}
