using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace HapiCopi
{
    public class MouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONUP = 0x0202;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private readonly LowLevelMouseProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public event Action<WordFormulaInfo>? WordFormulaClicked;

        private static int _cachedPid = -1;
        private static bool _isCachedWord = false;
        private static long _lastTriggerTick = 0;

        public MouseHook()
        {
            _proc = HookCallback;
            InstallHook();
        }

        private void InstallHook()
        {
            try
            {
                using (var curProcess = Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    if (curModule != null && !string.IsNullOrEmpty(curModule.ModuleName))
                    {
                        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                        Program.Log("Đã thiết lập MouseHook thành công.");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"Lỗi thiết lập MouseHook: {ex.Message}");
            }
        }

        private static bool IsWordForeground(IntPtr fgWnd)
        {
            if (fgWnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(fgWnd, out uint pid);
            if (pid == 0) return false;
            int ipid = (int)pid;
            if (ipid == _cachedPid) return _isCachedWord;

            try
            {
                using var proc = Process.GetProcessById(ipid);
                bool isWord = proc.ProcessName.Equals("WINWORD", StringComparison.OrdinalIgnoreCase);
                _cachedPid = ipid;
                _isCachedWord = isWord;
                return isWord;
            }
            catch
            {
                return false;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONUP)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        // Đợi Word hoàn tất cập nhật Selection sau khi nhả chuột
                        await Task.Delay(90);

                        IntPtr fgWnd = GetForegroundWindow();
                        if (!IsWordForeground(fgWnd)) return;

                        // Chống kích hoạt lặp liên tục trong 400ms (ví dụ double-click)
                        long now = Environment.TickCount64;
                        if (now - _lastTriggerTick < 400) return;

                        var formulaInfo = WordInterop.GetSelectedFormulaInfo();
                        if (formulaInfo != null && !string.IsNullOrEmpty(formulaInfo.Formula))
                        {
                            _lastTriggerTick = Environment.TickCount64;
                            WordFormulaClicked?.Invoke(formulaInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"[MouseHook] Lỗi kiểm tra selection click: {ex.Message}");
                    }
                });
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
