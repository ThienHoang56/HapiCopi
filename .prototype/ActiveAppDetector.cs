using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HapiCopiLite
{
    public class TargetAppInfo
    {
        public string ProcessName { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string WindowTitle { get; set; } = "";

        public bool IsMicrosoftWord => ClassName.Equals("OpusApp", StringComparison.OrdinalIgnoreCase) || 
                                       ProcessName.Equals("winword", StringComparison.OrdinalIgnoreCase);

        public bool IsGoogleDocs => WindowTitle.Contains("Google Docs", StringComparison.OrdinalIgnoreCase) || 
                                    WindowTitle.Contains("Google Tài liệu", StringComparison.OrdinalIgnoreCase);

        public bool IsAnyOfficeOrDocs => IsMicrosoftWord || IsGoogleDocs;

        public bool IsCodeEditor => ProcessName.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                                    ProcessName.Equals("cursor", StringComparison.OrdinalIgnoreCase) ||
                                    ProcessName.Equals("notepad", StringComparison.OrdinalIgnoreCase) ||
                                    ProcessName.Equals("notepad++", StringComparison.OrdinalIgnoreCase) ||
                                    ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase) ||
                                    ProcessName.Equals("sublime_text", StringComparison.OrdinalIgnoreCase);

        public bool IsLatexEditor => ProcessName.Equals("texmaker", StringComparison.OrdinalIgnoreCase) ||
                                     ProcessName.Equals("texstudio", StringComparison.OrdinalIgnoreCase) ||
                                     WindowTitle.Contains("Overleaf", StringComparison.OrdinalIgnoreCase);
    }

    public static class ActiveAppDetector
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        public static TargetAppInfo GetCurrentActiveApp()
        {
            var info = new TargetAppInfo();
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return info;

                // 1. Lấy Class Name
                var classSb = new StringBuilder(256);
                GetClassName(hwnd, classSb, classSb.Capacity);
                info.ClassName = classSb.ToString();

                // 2. Lấy Window Title
                var titleSb = new StringBuilder(512);
                GetWindowText(hwnd, titleSb, titleSb.Capacity);
                info.WindowTitle = titleSb.ToString();

                // 3. Lấy Process Name
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid > 0)
                {
                    using var proc = Process.GetProcessById((int)pid);
                    info.ProcessName = proc.ProcessName.ToLowerInvariant();
                }
            }
            catch
            {
                // Bỏ qua lỗi truy cập quyền
            }

            return info;
        }
    }
}
