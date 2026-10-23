using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Dịch vụ Đồng bộ Định dạng Tài liệu (StyleSync Service).
    /// Thay thế hoàn toàn cơ chế Đũa phép cũ bằng hệ thống quản lý hồ sơ tài liệu chuyên biệt:
    /// - Chế độ Thủ công (Manual): Người dùng nạp file .docx mẫu, hệ thống phân tích OpenXML và lưu thành Hồ sơ.
    /// - Chế độ Tự động (Auto): Tự động phát hiện Google Docs từ trình duyệt, tải ngầm file .docx và bóc tách cấu trúc.
    /// - Quản lý Hồ sơ độc lập (Profile-bound): Mỗi tài liệu có hồ sơ riêng, tránh "râu ông nọ cắm cằm bà kia".
    /// </summary>
    public static class StyleSyncService
    {
        private static readonly string ProfilesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HapiCopi", "Profiles");

        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        public static bool IsEnabled
        {
            get => (AppConfig.Get("STYLESYNC_ENABLED") ?? "false").ToLower() == "true";
            set => AppConfig.Set("STYLESYNC_ENABLED", value ? "true" : "false");
        }

        /// <summary>
        /// Chế độ đích khi Copy: "auto" | "word" | "docs".
        ///
        /// - Docs: CHỈ đồng bộ cấu trúc, để Google Docs tự áp style của tài liệu đích.
        /// - Word: áp typography đo từ file .docx (direct formatting).
        /// - Auto: theo phím tắt Docs (Ctrl+Alt+S) như hành vi cũ.
        /// </summary>
        public static string TargetMode
        {
            get
            {
                string value = (AppConfig.Get("STYLESYNC_TARGET") ?? "auto").Trim().ToLowerInvariant();
                return value is "word" or "docs" ? value : "auto";
            }
            set => AppConfig.Set("STYLESYNC_TARGET", value is "word" or "docs" ? value : "auto");
        }

        public static bool IsGoogleDocsTarget =>
            TargetMode == "docs" || (TargetMode == "auto" && SmartPasteService.IsGoogleDocsActive);

        public static event Action? TargetModeChanged;

        public static void SetTargetMode(string mode)
        {
            TargetMode = mode;
            TargetModeChanged?.Invoke();
            Program.Log($"[StyleSync] Chế độ đích: {TargetMode}.");
        }
        public static DocumentStructureTree? ActiveProfile { get; private set; } = null;
        public static string ActiveProfileName => ActiveProfile?.DocumentTitle ?? "Chưa nạp tài liệu";
        public static bool HasActiveProfile => ActiveProfile != null && ActiveProfile.IsValid;

        public static List<DocumentStructureTree> SavedProfiles { get; } = new();

        public static event Action<DocumentStructureTree?>? ActiveProfileChanged;
        public static event Action<bool>? StateChanged;

        static StyleSyncService()
        {
            try
            {
                if (!Directory.Exists(ProfilesDirectory))
                {
                    Directory.CreateDirectory(ProfilesDirectory);
                }
                ReloadSavedProfiles();
            }
            catch (Exception ex)
            {
                Program.Log($"[StyleSync Init LỖI] {ex.Message}");
            }
        }

        /// <summary>
        /// Nạp lại tất cả các hồ sơ đã lưu từ đĩa.
        /// </summary>
        public static void ReloadSavedProfiles()
        {
            SavedProfiles.Clear();
            if (!Directory.Exists(ProfilesDirectory)) return;

            string[] files = Directory.GetFiles(ProfilesDirectory, "*.json");
            foreach (string file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var profile = JsonSerializer.Deserialize<DocumentStructureTree>(json);
                    if (profile != null && profile.IsValid)
                    {
                        // Hồ sơ tạo trước bản cập nhật cấu trúc: tự đọc lại file nguồn nếu còn.
                        if (profile.Outline.Count == 0
                            && !string.IsNullOrWhiteSpace(profile.SourcePath)
                            && File.Exists(profile.SourcePath))
                        {
                            try
                            {
                                var refreshed = OpenXmlStructureReader.ReadFromDocx(profile.SourcePath!);
                                refreshed.DocumentTitle = profile.DocumentTitle;
                                refreshed.RenumberOutline = profile.RenumberOutline;

                                // Ghi đè đúng file JSON đang đọc (không gọi SaveProfile để tránh
                                // thêm trùng vào SavedProfiles khi đang duyệt danh sách).
                                string refreshedJson = JsonSerializer.Serialize(refreshed, new JsonSerializerOptions { WriteIndented = true });
                                File.WriteAllText(file, refreshedJson);

                                profile = refreshed;
                                Program.Log($"[StyleSync] Đã làm mới hồ sơ cũ \"{profile.DocumentTitle}\" từ file nguồn.");
                            }
                            catch (Exception ex)
                            {
                                Program.Log($"[StyleSync] Không làm mới được hồ sơ \"{profile.DocumentTitle}\": {ex.Message}");
                            }
                        }

                        SavedProfiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    Program.Log($"[StyleSync] Lỗi đọc hồ sơ {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            // Khôi phục hồ sơ kích hoạt lần trước nếu có ghi nhận trong config
            string? lastProfile = AppConfig.Get("STYLESYNC_ACTIVE_PROFILE");
            if (!string.IsNullOrEmpty(lastProfile))
            {
                var match = SavedProfiles.Find(p => string.Equals(p.DocumentTitle, lastProfile, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    ActiveProfile = match;
                    SmartPasteService.SyncFromStyleSync(match, IsEnabled);
                }
            }
        }

        /// <summary>
        /// Lưu hồ sơ tài liệu vào thư mục Profiles của HapiCopi.
        /// </summary>
        public static void SaveProfile(DocumentStructureTree profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.DocumentTitle)) return;

            try
            {
                if (!Directory.Exists(ProfilesDirectory))
                {
                    Directory.CreateDirectory(ProfilesDirectory);
                }

                string safeName = Regex.Replace(profile.DocumentTitle, @"[\\/:*?""<>|]", "_");
                string filePath = Path.Combine(ProfilesDirectory, $"{safeName}.json");

                string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);

                // Cập nhật danh sách in-memory
                int existingIdx = SavedProfiles.FindIndex(p => string.Equals(p.DocumentTitle, profile.DocumentTitle, StringComparison.OrdinalIgnoreCase));
                if (existingIdx >= 0)
                {
                    SavedProfiles[existingIdx] = profile;
                }
                else
                {
                    SavedProfiles.Add(profile);
                }

                Program.Log($"[StyleSync] Đã lưu hồ sơ \"{profile.DocumentTitle}\" vào đĩa ({filePath}).");
            }
            catch (Exception ex)
            {
                Program.Log($"[StyleSync] Lỗi lưu hồ sơ \"{profile.DocumentTitle}\": {ex.Message}");
            }
        }

        /// <summary>
        /// Xóa hồ sơ tài liệu khỏi hệ thống.
        /// </summary>
        public static bool DeleteProfile(string profileTitle)
        {
            try
            {
                string safeName = Regex.Replace(profileTitle, @"[\\/:*?""<>|]", "_");
                string filePath = Path.Combine(ProfilesDirectory, $"{safeName}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                SavedProfiles.RemoveAll(p => string.Equals(p.DocumentTitle, profileTitle, StringComparison.OrdinalIgnoreCase));

                if (ActiveProfile != null && string.Equals(ActiveProfile.DocumentTitle, profileTitle, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveProfile = null;
                    AppConfig.Set("STYLESYNC_ACTIVE_PROFILE", "");
                    ActiveProfileChanged?.Invoke(null);
                }
                return true;
            }
            catch (Exception ex)
            {
                Program.Log($"[StyleSync] Lỗi xóa hồ sơ: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// CHẾ ĐỘ 1: THỦ CÔNG (Manual)
        /// Nạp tài liệu mẫu .docx từ đường dẫn file do người dùng chọn hoặc kéo thả.
        /// </summary>
        public static DocumentStructureTree LoadManualProfile(string docxPath)
        {
            if (!File.Exists(docxPath))
            {
                throw new FileNotFoundException($"Không tìm thấy file tài liệu: {docxPath}");
            }

            Program.Log($"[StyleSync Manual] Đang phân tích file mẫu .docx: {docxPath}...");
            var stopwatch = Stopwatch.StartNew();

            var tree = OpenXmlStructureReader.ReadFromDocx(docxPath);
            stopwatch.Stop();

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(docxPath);
            tree.DocumentTitle = fileNameWithoutExt;

            SaveProfile(tree);
            SetActiveProfile(tree);

            Program.Log($"[StyleSync Manual] Phân tích hoàn tất trong {stopwatch.ElapsedMilliseconds} ms: " +
                        $"\"{tree.DocumentTitle}\" (Font: {tree.BodyStyle.FontName} {tree.BodyStyle.FontSize}pt, " +
                        $"Giãn dòng: {tree.BodyStyle.LineSpacing:0.##}, Căn lề: {tree.BodyStyle.Alignment}, " +
                        $"Sườn: {tree.Outline.Count} đề mục, " +
                        $"Danh sách: {tree.BulletLists.Count} cấp bullet / {tree.OrderedLists.Count} cấp số).");

            return tree;
        }

        /// <summary>
        /// CHẾ ĐỘ 2: TỰ ĐỘNG (Auto)
        /// Tự động lấy URL từ cửa sổ Google Docs đang hoạt động (hoặc URL chỉ định),
        /// tải ngầm file .docx và phân tích cấu trúc OpenXML.
        /// </summary>
        public static async Task<DocumentStructureTree> AutoSyncGoogleDocsAsync(
            string? explicitUrl = null, 
            Action<string>? statusCallback = null)
        {
            statusCallback?.Invoke("Đang phát hiện tài liệu Google Docs...");
            string docUrl = explicitUrl ?? "";

            if (string.IsNullOrWhiteSpace(docUrl))
            {
                // Thử tìm từ cửa sổ đang hoạt động
                var activeApp = ActiveWindowHelper.GetActiveAppInfo();
                if (activeApp.IsGoogleDocs && !string.IsNullOrEmpty(activeApp.BrowserUrl))
                {
                    docUrl = activeApp.BrowserUrl;
                }
                else
                {
                    // Thử đọc từ clipboard xem người dùng có vừa copy link không
                    try
                    {
                        string clipText = Clipboard.GetText();
                        if (clipText.Contains("docs.google.com/document/d/"))
                        {
                            docUrl = clipText.Trim();
                        }
                    }
                    catch { }
                }
            }

            if (string.IsNullOrWhiteSpace(docUrl) || !docUrl.Contains("docs.google.com/document/d/"))
            {
                throw new InvalidOperationException("Không phát hiện tài liệu Google Docs nào đang mở. Hãy mở Google Docs trên trình duyệt hoặc dán liên kết tài liệu.");
            }

            // Trích xuất Document ID
            var match = Regex.Match(docUrl, @"docs\.google\.com/document/d/([a-zA-Z0-9_-]+)");
            if (!match.Success)
            {
                throw new InvalidOperationException($"Không thể nhận diện Document ID từ URL: {docUrl}");
            }

            string docId = match.Groups[1].Value;
            string exportUrl = $"https://docs.google.com/document/d/{docId}/export?format=docx";
            Program.Log($"[StyleSync Auto] Phát hiện Doc ID: {docId}. Export URL: {exportUrl}");

            statusCallback?.Invoke("Đang tải dữ liệu cấu trúc .docx từ Google Docs...");

            byte[]? docxBytes = null;
            string detectedTitle = "Google Docs (" + docId.Substring(0, Math.Min(6, docId.Length)) + ")";

            // 1. Thử tải trực tiếp qua HttpClient (nếu tài liệu public hoặc chia sẻ qua link)
            try
            {
                using var response = await HttpClient.GetAsync(exportUrl);
                if (response.IsSuccessStatusCode)
                {
                    byte[] data = await response.Content.ReadAsByteArrayAsync();
                    // Kiểm tra magic header của file ZIP (PK\x03\x04 = 0x50, 0x4B, 0x03, 0x04)
                    if (data.Length > 4 && data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04)
                    {
                        docxBytes = data;
                        Program.Log($"[StyleSync Auto] Tải trực tiếp thành công ({data.Length} bytes).");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[StyleSync Auto] Tải trực tiếp không khả dụng: {ex.Message}. Chuyển sang cơ chế Browser Session.");
            }

            // 2. Nếu tài liệu riêng tư (cần đăng nhập Google), kích hoạt trình duyệt tải và tự động bắt file từ Downloads
            if (docxBytes == null)
            {
                statusCallback?.Invoke("Kích hoạt tải qua trình duyệt (phiên đăng nhập Google)...");
                string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                DateTime startTime = DateTime.UtcNow;

                // Mở URL tải trên trình duyệt mặc định của người dùng
                Process.Start(new ProcessStartInfo(exportUrl) { UseShellExecute = true });

                // Chờ file .docx mới xuất hiện trong thư mục Downloads (tối đa 12 giây)
                statusCallback?.Invoke("Đang đợi file tải về thư mục Downloads...");
                string? newlyDownloadedFile = null;

                for (int i = 0; i < 24; i++)
                {
                    await Task.Delay(500);

                    if (Directory.Exists(downloadsPath))
                    {
                        var dirInfo = new DirectoryInfo(downloadsPath);
                        var recentFiles = dirInfo.GetFiles("*.docx");
                        foreach (var f in recentFiles)
                        {
                            if (f.LastWriteTimeUtc >= startTime.AddSeconds(-2))
                            {
                                // Đảm bảo file không còn bị khóa ghi bởi trình duyệt (như .crdownload)
                                if (IsFileReady(f.FullName))
                                {
                                    newlyDownloadedFile = f.FullName;
                                    break;
                                }
                            }
                        }
                    }

                    if (newlyDownloadedFile != null) break;
                }

                if (newlyDownloadedFile != null)
                {
                    docxBytes = await File.ReadAllBytesAsync(newlyDownloadedFile);
                    detectedTitle = Path.GetFileNameWithoutExtension(newlyDownloadedFile);
                    Program.Log($"[StyleSync Auto] Đã bắt được file tải về: {newlyDownloadedFile} ({docxBytes.Length} bytes).");
                }
                else
                {
                    throw new TimeoutException("Không nhận được file .docx tải về từ trình duyệt trong thời gian quy định.");
                }
            }

            // 3. Phân tích OpenXML từ luồng byte
            statusCallback?.Invoke("Đang phân tích cấu trúc OpenXML...");
            using var memStream = new MemoryStream(docxBytes);
            var tree = OpenXmlStructureReader.ReadFromStream(memStream, detectedTitle);
            tree.DocumentTitle = detectedTitle;

            SaveProfile(tree);
            SetActiveProfile(tree);

            statusCallback?.Invoke($"Đã đồng bộ thành công hồ sơ: {detectedTitle}!");
            return tree;
        }

        private static bool IsFileReady(string filename)
        {
            try
            {
                using var inputStream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.None);
                return inputStream.Length > 0;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// Kích hoạt một hồ sơ tài liệu làm hồ sơ hiện hành.
        /// </summary>
        public static void SetActiveProfile(DocumentStructureTree profile)
        {
            // Mỗi lần kích hoạt hồ sơ, sườn đánh số phiên làm việc bắt đầu lại từ file mẫu.
            profile.ProjectedOutline.Clear();
            profile.ProjectedSiblingCounters.Clear();
            ActiveProfile = profile;
            IsEnabled = true;
            AppConfig.Set("STYLESYNC_ACTIVE_PROFILE", profile.DocumentTitle ?? "");
            ActiveProfileChanged?.Invoke(ActiveProfile);
            StateChanged?.Invoke(true);

            // Đồng bộ sang SmartPasteService để giữ tương thích toàn bộ hệ thống
            SmartPasteService.SyncFromStyleSync(profile, true);
        }

        /// <summary>
        /// Chọn hồ sơ theo tên tài liệu.
        /// </summary>
        public static bool SwitchProfile(string profileTitle)
        {
            var match = SavedProfiles.Find(p => string.Equals(p.DocumentTitle, profileTitle, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SetActiveProfile(match);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Hủy kích hoạt hồ sơ hiện hành (Trở về chế độ dán mặc định không can thiệp template).
        /// </summary>
        public static void DeactivateProfile()
        {
            ActiveProfile = null;
            IsEnabled = false;
            AppConfig.Set("STYLESYNC_ACTIVE_PROFILE", "");
            ActiveProfileChanged?.Invoke(null);
            SmartPasteService.SyncFromStyleSync(null, false);
            Program.Log("[StyleSync] Đã hủy kích hoạt hồ sơ tài liệu. Trở về chế độ mặc định.");
        }

        /// <summary>
        /// Bật / Tắt nhanh tính năng StyleSync.
        /// </summary>
        public static void ToggleEnabled()
        {
            IsEnabled = !IsEnabled;
            StateChanged?.Invoke(IsEnabled);
            SmartPasteService.SyncFromStyleSync(IsEnabled ? ActiveProfile : null, IsEnabled);
            Program.Log($"[StyleSync] Trạng thái bật/tắt: {IsEnabled}");
        }

        /// <summary>
        /// Áp dụng định dạng chuẩn của Hồ sơ tài liệu đang hoạt động vào nội dung HTML copy.
        /// - Lột sạch các style rác do Gemini tiêm vào (Google Sans Text !important).
        /// - Áp chuẩn Font, Cỡ chữ, Giãn dòng, Căn lề của Thân bài.
        /// - Áp chuẩn Tiêu đề H1-H6 từ Cây cấu trúc đã đo đạc.
        /// - Áp chuẩn Bảng biểu (Table) và Danh sách (List).
        /// </summary>
        public static string ApplyActiveProfileToHtml(string htmlFragment)
        {
            if (!IsEnabled || ActiveProfile == null || !ActiveProfile.IsValid)
            {
                return htmlFragment;
            }

            var tree = ActiveProfile;
            string processed = htmlFragment;

            // 1. LỘT BỎ STYLE RÁC TỪ GEMINI / CHATGPT
            // Gemini tiêm font-family: 'Google Sans Text' !important vào mọi thẻ
            processed = Regex.Replace(processed, @"font-family:\s*['""]?Google Sans Text['""]?[^;""']*(!important)?;\s*", "", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"style=""\s*""", "");

            // 2. CHUYỂN ĐỔI BẰNG CÂY CẤU TRÚC ĐO ĐẠC THỰC TẾ
            return SmartPasteService.TransformHtmlWithTree(processed, tree);
        }
    }
}
