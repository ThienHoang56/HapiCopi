using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HapiCopi
{
    public class ClipboardListener : IDisposable
    {
        private class ClipboardMonitorForm : Form
        {
            private const int WM_CLIPBOARDUPDATE = 0x031D;
            private readonly Action _onClipboardChanged;

            public ClipboardMonitorForm(Action onClipboardChanged)
            {
                _onClipboardChanged = onClipboardChanged;
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                try
                {
                    // Đăng ký làm cửa sổ chỉ nhận thông điệp (Message-only window) để ẩn hoàn toàn
                    NativeMethods.SetParent(this.Handle, NativeMethods.HWND_MESSAGE);
                    NativeMethods.AddClipboardFormatListener(this.Handle);
                    Program.Log("Đăng ký thành công AddClipboardFormatListener với Handle: " + this.Handle);
                }
                catch (Exception ex)
                {
                    Program.Log("Lỗi trong OnHandleCreated: " + ex.Message);
                }
            }

            protected override void OnHandleDestroyed(EventArgs e)
            {
                try
                {
                    NativeMethods.RemoveClipboardFormatListener(this.Handle);
                    Program.Log("Đã gỡ đăng ký RemoveClipboardFormatListener.");
                }
                catch (Exception ex)
                {
                    Program.Log("Lỗi trong OnHandleDestroyed: " + ex.Message);
                }
                base.OnHandleDestroyed(e);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_CLIPBOARDUPDATE)
                {
                    _onClipboardChanged();
                }
                base.WndProc(ref m);
            }
        }

        private static class NativeMethods
        {
            public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AddClipboardFormatListener(IntPtr hwnd);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        }

        private readonly ClipboardMonitorForm _form;

        public event EventHandler? ClipboardChanged;

        public ClipboardListener()
        {
            _form = new ClipboardMonitorForm(OnClipboardChanged);
            // Ép buộc Windows Forms tạo Handle ngay lập tức để kích hoạt OnHandleCreated
            IntPtr forceHandle = _form.Handle;
        }

        private void OnClipboardChanged()
        {
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _form.Dispose();
        }
    }
}
