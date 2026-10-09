using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HapiCopiLite
{
    static class Program
    {
        private static Mutex? _singleInstanceMutex;

        [STAThread]
        static void Main()
        {
            _singleInstanceMutex = new Mutex(true, @"Global\HapiCopiLite_SingleInstance_Mutex", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "HapiCopi Lite đang chạy ngầm ở khay hệ thống (System Tray)!",
                    "HapiCopi Lite",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new HapiCopiLiteContext());
            }
            finally
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
        }
    }

    public class HapiCopiLiteContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _pauseMenuItem;
        private readonly ClipboardMonitorWindow _monitorWindow;
        private readonly KeyboardHook _keyboardHook;
        private readonly SynchronizationContext _syncContext;

        private bool _isPaused = false;
        private volatile bool _isInternalClipboardWrite = false;

        private string _lastRawText = "";
        private string? _lastFormattedHtml = null;

        public static readonly string LogFilePath = Path.Combine(Path.GetTempPath(), "hapicopi_lite.log");

        public HapiCopiLiteContext()
        {
            _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            // 1. Menu khay hệ thống
            var contextMenu = new ContextMenuStrip();
            var titleItem = new ToolStripMenuItem("🛡️ HapiCopi Lite v1.0 (Core Engine)") { Enabled = false };
            titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);

            _pauseMenuItem = new ToolStripMenuItem("Tạm dừng bảo vệ", null, (s, e) => TogglePause());
            var logMenuItem = new ToolStripMenuItem("Xem Log thời gian thực...", null, (s, e) => OpenLogFile());
            var exitMenuItem = new ToolStripMenuItem("Thoát", null, (s, e) => ExitApp());

            contextMenu.Items.Add(titleItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(_pauseMenuItem);
            contextMenu.Items.Add(logMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitMenuItem);

            // 2. Tạo Icon khay hệ thống
            _notifyIcon = new NotifyIcon
            {
                Icon = CreateShieldIcon(),
                ContextMenuStrip = contextMenu,
                Text = "HapiCopi Lite - Local DLP & Smart Auto-Format",
                Visible = true
            };

            _notifyIcon.ShowBalloonTip(
                3500,
                "HapiCopi Lite đã kích hoạt!",
                "Ứng dụng đang bảo vệ Clipboard & tự động định dạng Markdown/PDF.\nChạy hoàn toàn cục bộ 100% trên RAM.",
                ToolTipIcon.Info);

            Log("==================================================");
            Log("HapiCopi Lite khởi động thành công!");

            // 3. Đăng ký Listener bắt sự kiện Clipboard hướng sự kiện (Win32 Event-Driven, 0% CPU)
            _monitorWindow = new ClipboardMonitorWindow(OnClipboardChanged);

            // 4. Đăng ký Hook bàn phím (Ctrl+V) để thực hiện JIT Context-Adaptation
            _keyboardHook = new KeyboardHook();
            _keyboardHook.PastePressed += OnPastePressed;
        }

        private void OnClipboardChanged()
        {
            if (_isInternalClipboardWrite || _isPaused) return;

            string originalText = "";
            try
            {
                if (Clipboard.ContainsText())
                {
                    originalText = Clipboard.GetText();
                }
            }
            catch (Exception ex)
            {
                Log($"Lỗi đọc Clipboard: {ex.Message}");
                return;
            }

            if (string.IsNullOrWhiteSpace(originalText)) return;

            string snippet = originalText.Replace("\r", " ").Replace("\n", " ");
            if (snippet.Length > 40) snippet = snippet.Substring(0, 40) + "...";
            Log($"[Clipboard] Phát hiện sao chép ({originalText.Length} ký tự): \"{snippet}\"");

            // -------------------------------------------------------------
            // BƯỚC 1: QUÉT BẢO MẬT DLP (API Keys, Token, CCCD, Phone, Card)
            // -------------------------------------------------------------
            var dlp = DlpScanner.Scan(originalText);
            string processedText = originalText;
            string? officeHtml = null;

            if (dlp.HasPii)
            {
                processedText = dlp.GetSanitizedText(originalText);
                Log($"[DLP CẢNH BÁO] Phát hiện: {dlp.GetSummary()}. Đã che giấu an toàn!");

                _notifyIcon.ShowBalloonTip(
                    3000,
                    "🛡️ HapiCopi Lite: Đã che thông tin nhạy cảm!",
                    $"Phát hiện: {dlp.GetSummary()}\nClipboard đã được che giấu tự động trước khi dán.",
                    ToolTipIcon.Warning);
            }

            // -------------------------------------------------------------
            // BƯỚC 2: TỰ ĐỘNG ĐỊNH DẠNG (Markdown -> Office HTML & PDF Healer)
            // -------------------------------------------------------------
            if (AutoFormatter.IsMarkdown(processedText))
            {
                Log("[Auto-Format] Nhận diện Markdown (Bảng biểu / Heading). Đang tạo Office CF_HTML...");
                officeHtml = AutoFormatter.ConvertToOfficeHtml(processedText);
            }
            else if (AutoFormatter.LooksLikePdfText(processedText))
            {
                Log("[Auto-Format] Nhận diện văn bản PDF bị bẻ dòng. Đang nối câu & lọc rác...");
                processedText = AutoFormatter.HealPdfText(processedText);
            }

            _lastRawText = processedText;
            _lastFormattedHtml = officeHtml;

            // Nếu dữ liệu có thay đổi (bị che PII hoặc tạo HTML Office), ghi đè ngay lập tức
            if (dlp.HasPii || officeHtml != null || processedText != originalText)
            {
                SafeSetClipboard(processedText, officeHtml);
            }
        }

        private void OnPastePressed()
        {
            if (_isPaused || string.IsNullOrEmpty(_lastRawText)) return;

            // Khi người dùng bấm Ctrl + V, soi nhanh ứng dụng tiền cảnh để dán kiểu tối ưu
            Task.Run(() =>
            {
                try
                {
                    var target = ActiveAppDetector.GetCurrentActiveApp();
                    Log($"[JIT Context] Dán vào ứng dụng: {target.ProcessName} (Word={target.IsMicrosoftWord}, Docs={target.IsGoogleDocs}, Code={target.IsCodeEditor})");

                    if (target.IsCodeEditor)
                    {
                        // Dán vào VS Code / Notepad: Ép dán Plain Text thuần, tuyệt đối không chèn HTML rác
                        _syncContext.Post(_ => SafeSetClipboard(_lastRawText, null), null);
                        Log("[JIT Context] Đã dán dạng Plain-Text thuần cho trình soạn thảo code.");
                    }
                    else if (target.IsLatexEditor && !string.IsNullOrEmpty(_lastFormattedHtml) && _lastFormattedHtml.Contains("<table"))
                    {
                        // Dán vào Overleaf / TeXstudio: Chuyển bảng sang mã nguồn LaTeX \begin{table}...
                        string latexTable = AutoFormatter.ConvertHtmlTableToLatex(_lastFormattedHtml);
                        _syncContext.Post(_ => SafeSetClipboard(latexTable, null), null);
                        Log("[JIT Context] Đã chuyển đổi bảng biểu sang mã LaTeX.");
                    }
                    else if (target.IsAnyOfficeOrDocs && !string.IsNullOrEmpty(_lastFormattedHtml))
                    {
                        // Dán vào MS Word / Google Docs: Đảm bảo có HTML phong phú
                        _syncContext.Post(_ => SafeSetClipboard(_lastRawText, _lastFormattedHtml), null);
                        Log("[JIT Context] Đã nạp HTML Office bảng kẻ ô cho Word/Docs.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[JIT Context Lỗi] {ex.Message}");
                }
            });
        }

        private void SafeSetClipboard(string text, string? html)
        {
            try
            {
                _isInternalClipboardWrite = true;
                var dataObj = new DataObject();
                dataObj.SetData(DataFormats.UnicodeText, text);
                if (!string.IsNullOrEmpty(html))
                {
                    dataObj.SetData(DataFormats.Html, html);
                }
                Clipboard.SetDataObject(dataObj, true);
            }
            catch (Exception ex)
            {
                Log($"[Clipboard Lỗi ghi] {ex.Message}");
            }
            finally
            {
                Task.Delay(100).ContinueWith(_ => _isInternalClipboardWrite = false);
            }
        }

        private void TogglePause()
        {
            _isPaused = !_isPaused;
            _pauseMenuItem.Text = _isPaused ? "Tiếp tục bảo vệ" : "Tạm dừng bảo vệ";
            _notifyIcon.Text = _isPaused ? "HapiCopi Lite (Đang tạm dừng)" : "HapiCopi Lite - Đang bảo vệ";
            _notifyIcon.ShowBalloonTip(2000, "HapiCopi Lite", _isPaused ? "Đã tạm dừng bảo vệ." : "Đã bật lại bảo vệ.", ToolTipIcon.Info);
            Log(_isPaused ? "Người dùng đã tạm dừng bảo vệ." : "Người dùng đã kích hoạt lại bảo vệ.");
        }

        private void OpenLogFile()
        {
            try
            {
                if (!File.Exists(LogFilePath)) File.WriteAllText(LogFilePath, "=== HapiCopi Lite Log ===\r\n");
                Process.Start(new ProcessStartInfo("notepad.exe", LogFilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không mở được log: {ex.Message}");
            }
        }

        private void ExitApp()
        {
            _notifyIcon.Visible = false;
            _monitorWindow.Dispose();
            _keyboardHook.Dispose();
            Application.Exit();
        }

        private static Icon CreateShieldIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(Color.FromArgb(14, 165, 233));
                g.FillEllipse(brush, 1, 1, 30, 30);
                using var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var textBrush = new SolidBrush(Color.White);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("H", font, textBrush, new RectangleF(0, 0, 32, 32), sf);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public static void Log(string msg)
        {
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n";
                File.AppendAllText(LogFilePath, line);
            }
            catch { }
        }
    }

    // =========================================================================
    // WIN32 CLIPBOARD LISTENER (Event-driven qua Message-Only Window, 0% CPU)
    // =========================================================================
    internal class ClipboardMonitorWindow : Form
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private readonly Action _onChanged;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        public ClipboardMonitorWindow(Action onChanged)
        {
            _onChanged = onChanged;
            IntPtr forceHandle = this.Handle; // Buộc tạo Handle ngay
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetParent(this.Handle, new IntPtr(-3)); // HWND_MESSAGE: Cửa sổ ẩn chỉ nhận message
            AddClipboardFormatListener(this.Handle);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            RemoveClipboardFormatListener(this.Handle);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                _onChanged();
            }
            base.WndProc(ref m);
        }
    }

    // =========================================================================
    // LOW-LEVEL KEYBOARD HOOK (Bắt phím Ctrl+V & Shift+Insert cho JIT Adaptation)
    // =========================================================================
    internal class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_V = 0x56;
        private const int VK_INSERT = 0x2D;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public event Action? PastePressed;

        public KeyboardHook()
        {
            _proc = HookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == VK_V && (GetKeyState(VK_CONTROL) & 0x8000) != 0)
                {
                    PastePressed?.Invoke();
                }
                else if (vkCode == VK_INSERT && (GetKeyState(VK_SHIFT) & 0x8000) != 0)
                {
                    PastePressed?.Invoke();
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }
}
