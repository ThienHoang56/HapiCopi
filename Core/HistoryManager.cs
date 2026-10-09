using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HapiCopi
{
    /// <summary>
    /// Quản lý việc nạp, lưu, di trú và mã hóa (DPAPI) lịch sử clipboard cục bộ (Local-First).
    /// </summary>
    public static class HistoryManager
    {
        private static string ConfigDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HapiCopi");

        private static string DatPath => Path.Combine(ConfigDirectory, "history.dat");
        private static string JsonPath => Path.Combine(ConfigDirectory, "history.json");

        // Muối bảo mật (entropy) để tăng cường bảo vệ DPAPI
        private static byte[] GetOptionalEntropy()
        {
            return new byte[] { 0x48, 0x61, 0x70, 0x69, 0x43, 0x6F, 0x70, 0x69, 0x5F, 0x53, 0x65, 0x63, 0x75, 0x72, 0x65, 0x5F, 0x44, 0x50, 0x41, 0x50, 0x49 };
        }

        /// <summary>
        /// Nạp lịch sử clipboard từ đĩa khi khởi động (tự động phát hiện định dạng mã hóa hoặc văn bản rõ).
        /// </summary>
        public static List<ClipboardHistoryItem> LoadHistory()
        {
            var list = new List<ClipboardHistoryItem>();

            // Nếu cấu hình lưu đĩa bị tắt, hoạt động RAM-only
            if ((AppConfig.Get("SAVE_HISTORY_TO_DISK") ?? "true").ToLower() == "false")
            {
                Program.Log("[HistoryManager] Chế độ lưu đĩa đang tắt. Lịch sử hoạt động RAM-Only.");
                return list;
            }

            try
            {
                Directory.CreateDirectory(ConfigDirectory);

                // Trường hợp 1: Có file mã hóa .dat, tiến hành giải mã
                if (File.Exists(DatPath))
                {
                    Program.Log("[HistoryManager] Tìm thấy file lịch sử mã hóa history.dat. Đang giải mã DPAPI...");
                    byte[] encryptedBytes = File.ReadAllBytes(DatPath);
                    byte[] entropy = GetOptionalEntropy();
                    byte[] plaintextBytes = ProtectedData.Unprotect(encryptedBytes, entropy, DataProtectionScope.CurrentUser);
                    
                    string json = Encoding.UTF8.GetString(plaintextBytes);
                    list = JsonSerializer.Deserialize<List<ClipboardHistoryItem>>(json) ?? new List<ClipboardHistoryItem>();
                    Program.Log($"[HistoryManager] Đã nạp thành công {list.Count} mục lịch sử đã mã hóa.");
                }
                // Trường hợp 2: Có file plaintext .json, nạp văn bản rõ
                else if (File.Exists(JsonPath))
                {
                    Program.Log("[HistoryManager] Tìm thấy file lịch sử văn bản rõ history.json. Đang nạp...");
                    string json = File.ReadAllText(JsonPath, Encoding.UTF8);
                    list = JsonSerializer.Deserialize<List<ClipboardHistoryItem>>(json) ?? new List<ClipboardHistoryItem>();
                    Program.Log($"[HistoryManager] Đã nạp thành công {list.Count} mục lịch sử từ file văn bản rõ.");
                }

                // Làm sạch và thu gọn các chuỗi Base64 cũ còn sót lại trong lịch sử để tiết kiệm RAM
                foreach (var item in list)
                {
                    item.OriginalText = HapiCopiApplicationContext.SanitizeTextForHistory(item.OriginalText);
                    item.OriginalHtml = HapiCopiApplicationContext.SanitizeHtmlForHistory(item.OriginalHtml);
                    item.ProcessedText = HapiCopiApplicationContext.SanitizeTextForHistory(item.ProcessedText);
                    item.ProcessedHtml = HapiCopiApplicationContext.SanitizeHtmlForHistory(item.ProcessedHtml);
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[HistoryManager LỖI] Lỗi nạp và giải mã lịch sử clipboard: {ex.Message}");
                // Trong trường hợp giải mã thất bại (do đổi tài khoản Windows hoặc file lỗi),
                // trả về danh sách rỗng để tránh làm sập ứng dụng.
            }

            return list;
        }

        /// <summary>
        /// Lưu trữ lịch sử clipboard thích ứng theo cấu hình hiện hành.
        /// </summary>
        public static void SaveHistory(List<ClipboardHistoryItem> history)
        {
            // Kiểm tra cấu hình có cho phép lưu đĩa không
            if ((AppConfig.Get("SAVE_HISTORY_TO_DISK") ?? "true").ToLower() == "false")
            {
                DeletePhysicalFiles();
                return;
            }

            try
            {
                Directory.CreateDirectory(ConfigDirectory);

                // Bảo vệ nhạy cảm trước khi ghi đĩa (PII Protection)
                bool maskHistory = (AppConfig.Get("PII_MASK_HISTORY") ?? "true").ToLower() != "false";
                var historyToSave = new List<ClipboardHistoryItem>();

                foreach (var item in history)
                {
                    var clone = new ClipboardHistoryItem
                    {
                        Timestamp = item.Timestamp,
                        AppName = item.AppName,
                        OriginalText = item.OriginalText,
                        OriginalHtml = item.OriginalHtml,
                        ProcessedText = item.ProcessedText,
                        ProcessedHtml = item.ProcessedHtml,
                        Status = item.Status,
                        PiiResult = item.PiiResult
                    };
                    
                    // Copy ID của item để giữ nguyên định danh
                    clone.Id = item.Id;

                    foreach (var kv in item.ClipboardFormats)
                    {
                        clone.ClipboardFormats[kv.Key] = kv.Value;
                    }

                    // Nếu bật che lịch sử và mục lịch sử chứa PII nhạy cảm
                    if (maskHistory && item.PiiResult != null && item.PiiResult.HasPii)
                    {
                        // Xóa vĩnh viễn dữ liệu gốc nhạy cảm, chỉ lưu trữ bản đã được che sạch
                        clone.OriginalText = item.ProcessedText;
                        clone.OriginalHtml = "";
                    }

                    historyToSave.Add(clone);
                }

                string json = JsonSerializer.Serialize(historyToSave);

                // Kiểm tra cấu hình mã hóa bảo mật
                if ((AppConfig.Get("ENCRYPT_HISTORY_DATA") ?? "true").ToLower() != "false")
                {
                    // Chế độ mã hóa: Sử dụng DPAPI bảo vệ
                    byte[] plaintextBytes = Encoding.UTF8.GetBytes(json);
                    byte[] entropy = GetOptionalEntropy();
                    byte[] encryptedBytes = ProtectedData.Protect(plaintextBytes, entropy, DataProtectionScope.CurrentUser);
                    
                    File.WriteAllBytes(DatPath, encryptedBytes);
                    
                    // Xóa file JSON rõ cũ nếu có để tránh rò rỉ
                    if (File.Exists(JsonPath)) File.Delete(JsonPath);
                }
                else
                {
                    // Chế độ văn bản rõ: Lưu trực tiếp
                    File.WriteAllText(JsonPath, json, Encoding.UTF8);
                    
                    // Xóa file mã hóa cũ nếu có
                    if (File.Exists(DatPath)) File.Delete(DatPath);
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[HistoryManager LỖI] Lỗi khi lưu trữ lịch sử clipboard xuống đĩa: {ex.Message}");
            }
        }

        /// <summary>
        /// Thực hiện di trú dữ liệu nóng (Hot-Migration) ngay lập tức khi cấu hình lưu trữ/mã hóa thay đổi.
        /// </summary>
        public static void MigrateHistoryData(List<ClipboardHistoryItem> currentHistory)
        {
            Program.Log("[HistoryManager] Kích hoạt tiến trình di trú dữ liệu nóng (Hot-Migration)...");
            SaveHistory(currentHistory);
        }

        /// <summary>
        /// Xóa bỏ vật lý các file lịch sử lưu trên đĩa.
        /// </summary>
        public static void DeletePhysicalFiles()
        {
            try
            {
                if (File.Exists(DatPath))
                {
                    File.Delete(DatPath);
                    Program.Log("[HistoryManager] Đã xóa file mã hóa history.dat trên đĩa.");
                }
                if (File.Exists(JsonPath))
                {
                    File.Delete(JsonPath);
                    Program.Log("[HistoryManager] Đã xóa file văn bản rõ history.json trên đĩa.");
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[HistoryManager LỖI] Lỗi xóa file lịch sử vật lý: {ex.Message}");
            }
        }
    }
}
