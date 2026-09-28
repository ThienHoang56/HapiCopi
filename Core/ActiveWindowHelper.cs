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

        public static string GetActiveBrowserUrlWithTimeout(string processName, IntPtr hwnd, int timeoutMs = 300)
        {
            string url = "";
            var thread = new System.Threading.Thread(() =>
            {
                url = GetActiveBrowserUrl(processName, hwnd);
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            if (!thread.Join(timeoutMs))
            {
                Program.Log($"[UIA] Quá thời hạn truy vấn URL ({timeoutMs}ms) cho {processName}");
                try { thread.Interrupt(); } catch {}
            }
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

                // 4. Lấy Browser URL nếu là trình duyệt
                if (info.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                    info.ProcessName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
                    info.ProcessName.Equals("firefox", StringComparison.OrdinalIgnoreCase))
                {
                    info.BrowserUrl = GetActiveBrowserUrlWithTimeout(info.ProcessName, hwnd, 300);
                    Program.Log($"[ActiveWindowHelper] Query URL hoàn tất: \"{info.BrowserUrl}\"");
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
