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

        private const int SM_CXDOUBLECLK = 36;
        private const int SM_CYDOUBLECLK = 37;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

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

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private readonly LowLevelMouseProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public event Action<WordFormulaInfo>? WordFormulaDoubleClicked;
        public event Action<IntPtr, ActiveAppInfo>? WebFormulaDoubleClicked;

        private static long _lastClickTick = 0;
        private static int _lastClickX = -9999;
        private static int _lastClickY = -9999;

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

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONUP)
            {
                int clickX = 0, clickY = 0;
                try
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    clickX = hookStruct.pt.x;
                    clickY = hookStruct.pt.y;
                }
                catch { }

                long now = Environment.TickCount64;
                int maxTime = (int)GetDoubleClickTime();
                if (maxTime <= 0) maxTime = 500;
                int maxDx = GetSystemMetrics(SM_CXDOUBLECLK);
                if (maxDx <= 0) maxDx = 4;
                int maxDy = GetSystemMetrics(SM_CYDOUBLECLK);
                if (maxDy <= 0) maxDy = 4;

                bool isDblClick = (now - _lastClickTick <= maxTime) &&
                                  (Math.Abs(clickX - _lastClickX) <= maxDx) &&
                                  (Math.Abs(clickY - _lastClickY) <= maxDy);

                if (isDblClick)
                {
                    _lastClickTick = 0;
                    _lastClickX = -9999;
                    _lastClickY = -9999;

                    Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(100);
                            IntPtr fgWnd = GetForegroundWindow();
                            ActiveAppInfo activeApp = ActiveWindowHelper.GetActiveAppInfo();

                            if (activeApp.IsMicrosoftWord || activeApp.ProcessName.Equals("WINWORD", StringComparison.OrdinalIgnoreCase))
                            {
                                var formulaInfo = WordInterop.GetSelectedFormulaInfo();
                                if (formulaInfo != null && !string.IsNullOrEmpty(formulaInfo.Formula))
                                {
                                    WordFormulaDoubleClicked?.Invoke(formulaInfo);
                                }
                            }
                            else if (activeApp.IsGoogleDocs ||
                                     activeApp.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                                     activeApp.ProcessName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
                                     activeApp.ProcessName.Equals("brave", StringComparison.OrdinalIgnoreCase) ||
                                     activeApp.ProcessName.Equals("firefox", StringComparison.OrdinalIgnoreCase))
                            {
                                WebFormulaDoubleClicked?.Invoke(fgWnd, activeApp);
                            }
                        }
                        catch (Exception ex)
                        {
                            Program.Log($"[MouseHook] Lỗi xử lý double-click: {ex.Message}");
                        }
                    });
                }
                else
                {
                    _lastClickTick = now;
                    _lastClickX = clickX;
                    _lastClickY = clickY;
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
