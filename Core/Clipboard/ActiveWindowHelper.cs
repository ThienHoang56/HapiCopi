using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace HapiCopi
{
    public class ActiveAppInfo
    {
        public IntPtr WindowHandle { get; set; } = IntPtr.Zero;
        public string ProcessName { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public string BrowserUrl { get; set; } = "";

        public bool IsMicrosoftWord => ClassName.Equals("OpusApp", StringComparison.OrdinalIgnoreCase);

        public bool IsWordOnline
        {
            get
            {
                bool isBrowser = ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase) || 
                                 ProcessName.Equals("msedge", StringComparison.OrdinalIgnoreCase) || 
                                 ProcessName.Equals("firefox", StringComparison.OrdinalIgnoreCase);
                if (!isBrowser) return false;

                if (WindowTitle.Contains(".docx", StringComparison.OrdinalIgnoreCase) || 
                    WindowTitle.Contains("- Word", StringComparison.OrdinalIgnoreCase) || 
                    WindowTitle.Contains("SharePoint", StringComparison.OrdinalIgnoreCase) || 
                    WindowTitle.Contains("Office.com", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(BrowserUrl))
                {
                    string urlLower = BrowserUrl.ToLowerInvariant();
                    if (urlLower.Contains("sharepoint.com") || 
                        urlLower.Contains("office.com") || 
                        urlLower.Contains("office365.com"))
                    {
                        if (urlLower.Contains("/w/") || urlLower.Contains("doc.aspx") || urlLower.Contains("word"))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public bool IsAnyWord => IsMicrosoftWord || IsWordOnline;
        
        public bool IsGoogleDocs
        {
            get
            {
                if (WindowTitle.Contains("Google Docs", StringComparison.OrdinalIgnoreCase) || 
                    WindowTitle.Contains("Google Tài liệu", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(BrowserUrl))
                {
                    string urlLower = BrowserUrl.ToLowerInvariant();
                    if (urlLower.Contains("docs.google.com/document"))
                    {
                        return true;
                    }
                }
                return false;
            }
        }
                                     
        public bool IsLaTeXEditor => ProcessName.Equals("texmaker", StringComparison.OrdinalIgnoreCase) || 
                                     ProcessName.Equals("texstudio", StringComparison.OrdinalIgnoreCase) ||
                                     WindowTitle.Contains("Overleaf", StringComparison.OrdinalIgnoreCase);
                                     
        public bool IsNotepadOrEditor => ProcessName.Equals("notepad", StringComparison.OrdinalIgnoreCase) ||
                                          ProcessName.Equals("notepad++", StringComparison.OrdinalIgnoreCase);

        public bool IsVSCode => ProcessName.Equals("Code", StringComparison.OrdinalIgnoreCase);
    }

    public static class ActiveWindowHelper
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_SHOW = 5;

        public static void ForceForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                IntPtr foreWnd = GetForegroundWindow();
                uint foreThread = GetWindowThreadProcessId(foreWnd, out _);
                uint currentThread = GetCurrentThreadId();

                if (foreThread != 0 && foreThread != currentThread)
                {
                    AttachThreadInput(currentThread, foreThread, true);
                    BringWindowToTop(hWnd);
                    ShowWindow(hWnd, SW_SHOW);
                    SetForegroundWindow(hWnd);
                    AttachThreadInput(currentThread, foreThread, false);
                }
                else
                {
                    BringWindowToTop(hWnd);
                    ShowWindow(hWnd, SW_SHOW);
                    SetForegroundWindow(hWnd);
                }

                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
            catch (Exception ex)
            {
                Program.Log($"[ActiveWindowHelper] ForceForeground lỗi: {ex.Message}");
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        public static string GetActiveBrowserUrl(string processName, IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return "";
                
                Program.Log($"[UIA] Bắt đầu truy vấn URL cho {processName} (hwnd={hwnd})");
                AutomationElement root = AutomationElement.FromHandle(hwnd);
                if (root == null)
                {
                    Program.Log("[UIA] Không thể lấy root element từ hwnd");
                    return "";
                }

                // Find edit controls (address bar)
                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                var edits = root.FindAll(TreeScope.Descendants, condition);
                Program.Log($"[UIA] Tìm thấy {edits.Count} phần tử ControlType.Edit trong {processName}");
                
                foreach (AutomationElement edit in edits)
                {
                    if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                    {
                        var valPattern = (ValuePattern)pattern;
                        string val = valPattern.Current.Value;
                        if (!string.IsNullOrEmpty(val))
                        {
                            val = val.Trim();
                            Program.Log($"[UIA] Giá trị của edit: \"{val}\"");
                            if (val.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                val.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                val.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) ||
                                val.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                                val.Contains("docs.google.com") ||
                                val.Contains("google.com/document") ||
                                val.Contains("sharepoint.com") ||
                                val.Contains("office.com") ||
                                val.Contains("office365.com"))
                            {
                                Program.Log($"[UIA] Tìm thấy URL khớp: \"{val}\"");
                                return val;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[UIA LỖI] {ex.Message}\n{ex.StackTrace}");
            }
            return "";
        }

        private static IntPtr _lastUrlHwnd = IntPtr.Zero;
        private static string _lastCachedUrl = "";
        private static DateTime _lastUrlTime = DateTime.MinValue;

        public static string GetActiveBrowserUrlWithTimeout(string processName, IntPtr hwnd, int timeoutMs = 250)
        {
            if (hwnd == IntPtr.Zero) return "";
            if (hwnd == _lastUrlHwnd && (DateTime.Now - _lastUrlTime).TotalSeconds < 4)
            {
                return _lastCachedUrl;
            }

            string url = "";
            try
            {
                var task = System.Threading.Tasks.Task.Run(() => GetActiveBrowserUrl(processName, hwnd));
                if (task.Wait(timeoutMs))
                {
                    url = task.Result;
                    _lastUrlHwnd = hwnd;
                    _lastCachedUrl = url;
                    _lastUrlTime = DateTime.Now;
                }
                else
                {
                    Program.Log($"[UIA] Quá thời hạn truy vấn URL ({timeoutMs}ms) cho {processName}");
                }
            }
            catch { }
            return url;
        }

        public static ActiveAppInfo GetActiveAppInfo()
        {
            var info = new ActiveAppInfo();
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return info;
                info.WindowHandle = hwnd;

                // 1. Lấy Class Name
                var classNameBuilder = new StringBuilder(256);
                if (GetClassName(hwnd, classNameBuilder, classNameBuilder.Capacity) > 0)
                {
                    info.ClassName = classNameBuilder.ToString();
                }

                // 2. Lấy Window Title
                var titleBuilder = new StringBuilder(512);
                if (GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity) > 0)
                {
                    info.WindowTitle = titleBuilder.ToString();
                }

                // 3. Lấy Process Name
                uint processId;
                GetWindowThreadProcessId(hwnd, out processId);
                if (processId > 0)
                {
                    using (var process = System.Diagnostics.Process.GetProcessById((int)processId))
                    {
                        info.ProcessName = process.ProcessName;
                    }
                }

                // 4. Lấy Browser URL nếu là trình duyệt (Chỉ khi cần thiết và WindowTitle chưa đủ để nhận diện)
                if (info.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                    info.ProcessName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
                    info.ProcessName.Equals("firefox", StringComparison.OrdinalIgnoreCase))
                {
                    // Fast-path: Nếu tiêu đề tab trình duyệt đã có tên Google Docs hoặc Word thì bỏ qua UI Automation hoàn toàn
                    bool alreadyRecognized = info.WindowTitle.Contains("Google Docs", StringComparison.OrdinalIgnoreCase) ||
                                             info.WindowTitle.Contains("Google Tài liệu", StringComparison.OrdinalIgnoreCase) ||
                                             info.WindowTitle.Contains(".docx", StringComparison.OrdinalIgnoreCase) ||
                                             info.WindowTitle.Contains("- Word", StringComparison.OrdinalIgnoreCase);

                    if (!alreadyRecognized)
                    {
                        info.BrowserUrl = GetActiveBrowserUrlWithTimeout(info.ProcessName, hwnd, 150);
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[ActiveWindowHelper LỖI] {ex.Message}\n{ex.StackTrace}");
            }
            return info;
        }
    }
}
