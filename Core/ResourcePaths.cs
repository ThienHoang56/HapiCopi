using System;
using System.IO;

namespace HapiCopi
{
    /// <summary>
    /// Đường dẫn tài nguyên WebView (HTML/JS) — cài đặt, publish, hoặc chạy debug.
    /// </summary>
    public static class ResourcePaths
    {
        public const string AppDataFolderName = "HapiCopi";
        public const string WebViewFolderName = "WebViewResources";

        /// <summary>Thư mục dữ liệu user: %LocalAppData%\HapiCopi</summary>
        public static string AppDataRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataFolderName);

        /// <summary>Cache WebView2 Chromium</summary>
        public static string WebViewCacheDir =>
            Path.Combine(AppDataRoot, "WebViewCache");

        /// <summary>Nhật ký chạy: %LocalAppData%\HapiCopi\Logs</summary>
        public static string LogDirectory => Path.Combine(AppDataRoot, "Logs");

        /// <summary>Cấu hình, lịch sử, danh sách loại trừ: %AppData%\HapiCopi</summary>
        public static string RoamingDataRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataFolderName);

        /// <summary>
        /// Thư mục chứa index.html / matheditor…
        /// Ưu tiên: cạnh exe (bộ cài) → AppData → thư mục source khi debug.
        /// </summary>
        public static string GetWebViewResourcesFolder()
        {
            string besideExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WebViewFolderName);
            if (IsValidWebViewFolder(besideExe))
                return besideExe;

            string inAppData = Path.Combine(AppDataRoot, WebViewFolderName);
            if (IsValidWebViewFolder(inAppData))
                return inAppData;

            // Debug: bin/Debug/netX-windows → lên 3 cấp về root project
            try
            {
                var baseDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                var projectRoot = baseDir.Parent?.Parent?.Parent;
                if (projectRoot != null)
                {
                    string dev = Path.Combine(projectRoot.FullName, WebViewFolderName);
                    if (IsValidWebViewFolder(dev))
                        return dev;
                }
            }
            catch { /* ignore */ }

            return besideExe;
        }

        private static bool IsValidWebViewFolder(string path) =>
            Directory.Exists(path) && File.Exists(Path.Combine(path, "index.html"));
    }
}
