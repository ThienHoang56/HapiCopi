using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HapiCopi
{
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_V = 0x56;
        private const int VK_INSERT = 0x2D;
        private const int VK_M = 0x4D;

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

        public event Action? PastePressed;
        public event Action? AutoFormatPressed;

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

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                
                // 1. Kiểm tra tổ hợp Ctrl + V
                if (vkCode == VK_V)
                {
                    bool ctrlPressed = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                    if (ctrlPressed)
                    {
                        // Kích hoạt bất đồng bộ để tránh chặn luồng bàn phím hệ thống
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
                // 3. Kiểm tra tổ hợp Ctrl + M
                else if (vkCode == VK_M)
                {
                    bool ctrlPressed = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                    if (ctrlPressed)
                    {
                        System.Threading.Tasks.Task.Run(() => AutoFormatPressed?.Invoke());
                    }
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
                Program.Log("Đã gỡ bỏ KeyboardHook.");
            }
        }
    }
}
