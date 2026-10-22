using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HapiCopi
{
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        
        private const int VK_CONTROL = 0x11;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12; // Phím Alt
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_V = 0x56;
        private const int VK_INSERT = 0x2D;
        private const int VK_M = 0x4D; // Giữ tương thích Ctrl+Alt+M
        private const int VK_P = 0x50; // Phím P (Tạm dừng / Bật lại như Unikey)
        private const int VK_Q = 0x51; // Phím Q (Thoát hoàn toàn app)
        private const int VK_PAUSE = 0x13; // Phím Pause/Break

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        // Trạng thái theo dõi tổ hợp Control + Alt (KeyUp sạch)
        private bool _ctrlAltComboPending = false;
        private bool _ctrlAltThirdKeyPressed = false;

        // Chống lặp phím tự động (typematic autorepeat debounce)
        private DateTime _lastPauseToggleTime = DateTime.MinValue;
        private DateTime _lastExitPressTime = DateTime.MinValue;

        public event Action? PastePressed;
        public event Action? SmartPastePressed;
        public event Action? MathHotkeyPressed;
        public event Action? PauseTogglePressed;
        public event Action? ExitAppPressed;

        public KeyboardHook()
        {
            _proc = HookCallback;
            InstallHook();
        }

        private void InstallHook()
        {
            try
            {
                using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    if (curModule != null && !string.IsNullOrEmpty(curModule.ModuleName))
                    {
                        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                        Program.Log("Đã thiết lập KeyboardHook thành công.");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"Lỗi thiết lập KeyboardHook: {ex.Message}");
            }
        }

        private static bool IsControlKey(int vk) => vk == VK_CONTROL || vk == VK_LCONTROL || vk == VK_RCONTROL;
        private static bool IsAltKey(int vk) => vk == VK_MENU || vk == VK_LMENU || vk == VK_RMENU;

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                int vkCode = Marshal.ReadInt32(lParam);

                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    bool ctrlDown = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool altDown = (GetKeyState(VK_MENU) & 0x8000) != 0;

                    if (IsControlKey(vkCode))
                    {
                        if (altDown)
                        {
                            _ctrlAltComboPending = true;
                            _ctrlAltThirdKeyPressed = false;
                        }
                    }
                    else if (IsAltKey(vkCode))
                    {
                        if (ctrlDown)
                        {
                            _ctrlAltComboPending = true;
                            _ctrlAltThirdKeyPressed = false;
                        }
                    }
                    else
                    {
                        // Người dùng đã nhấn phím thứ ba khác
                        _ctrlAltThirdKeyPressed = true;
                        _ctrlAltComboPending = false;

                        // 1. Kiểm tra tổ hợp Ctrl + V hoặc Ctrl + Shift + V (Dán thông minh Magic Wand)
                        if (vkCode == VK_V && ctrlDown && !altDown)
                        {
                            bool shiftDown = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                            if (shiftDown)
                            {
                                System.Threading.Tasks.Task.Run(() => SmartPastePressed?.Invoke());
                            }
                            else
                            {
                                System.Threading.Tasks.Task.Run(() => PastePressed?.Invoke());
                            }
                        }
                        // 2. Kiểm tra tổ hợp Shift + Insert
                        else if (vkCode == VK_INSERT)
                        {
                            bool shiftPressed = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                            if (shiftPressed)
                            {
                                System.Threading.Tasks.Task.Run(() => PastePressed?.Invoke());
                            }
                        }
                        // 3. Giữ tương thích thêm cho tổ hợp Ctrl + Alt + M
                        else if (vkCode == VK_M && ctrlDown && altDown)
                        {
                            System.Threading.Tasks.Task.Run(() => MathHotkeyPressed?.Invoke());
                        }
                        // 4. Phím tắt Tạm dừng / Tiếp tục: Ctrl + Alt + P (hoặc Ctrl + Pause)
                        else if ((vkCode == VK_P && ctrlDown && altDown) || (vkCode == VK_PAUSE && ctrlDown))
                        {
                            var now = DateTime.UtcNow;
                            if ((now - _lastPauseToggleTime).TotalMilliseconds > 400)
                            {
                                _lastPauseToggleTime = now;
                                System.Threading.Tasks.Task.Run(() => PauseTogglePressed?.Invoke());
                            }
                        }
                        // 5. Phím tắt Thoát app hoàn toàn: Ctrl + Alt + Q
                        else if (vkCode == VK_Q && ctrlDown && altDown)
                        {
                            var now = DateTime.UtcNow;
                            if ((now - _lastExitPressTime).TotalMilliseconds > 1000)
                            {
                                _lastExitPressTime = now;
                                System.Threading.Tasks.Task.Run(() => ExitAppPressed?.Invoke());
                            }
                        }
                    }
                }
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    if (_ctrlAltComboPending && !_ctrlAltThirdKeyPressed)
                    {
                        if (IsControlKey(vkCode) || IsAltKey(vkCode))
                        {
                            _ctrlAltComboPending = false;
                            System.Threading.Tasks.Task.Run(() => MathHotkeyPressed?.Invoke());
                        }
                    }

                    bool ctrlDown = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool altDown = (GetKeyState(VK_MENU) & 0x8000) != 0;
                    if (!ctrlDown && !altDown)
                    {
                        _ctrlAltComboPending = false;
                        _ctrlAltThirdKeyPressed = false;
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public bool IsHookActive => _hookId != IntPtr.Zero;

        public void EnsureHookActive()
        {
            if (_hookId == IntPtr.Zero)
            {
                Program.Log("[KeyboardHook] Hook chưa kích hoạt hoặc bị Windows ngắt, đang thiết lập lại...");
                InstallHook();
            }
        }

        public void ReinstallHook()
        {
            try
            {
                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                    _hookId = IntPtr.Zero;
                }
            }
            catch { }
            InstallHook();
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                Program.Log("Đã gỡ bỏ KeyboardHook.");
            }
        }
    }
}

