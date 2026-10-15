using System.Drawing;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Mở soạn toán/nhạc: nạp WebView sẵn ngoài màn hình lúc app khởi động,
    /// lần bấm menu chỉ đưa cửa sổ vào giữa — không chờ Chromium lần nữa.
    /// </summary>
    internal static class EditorWindow
    {
        public static void PrepareOffscreen(Form form)
        {
            IntPtr prev = ActiveWindowHelper.GetForegroundWindow();
            form.Opacity = 0;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.Show();
            if (prev != IntPtr.Zero && prev != form.Handle)
            {
                ActiveWindowHelper.SetForegroundWindow(prev);
            }
        }

        public static void Present(Form form)
        {
            form.Opacity = 1;
            form.WindowState = FormWindowState.Normal;
            form.StartPosition = FormStartPosition.Manual;
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            form.Location = new Point(
                area.Left + Math.Max(0, (area.Width - form.Width) / 2),
                area.Top + Math.Max(0, (area.Height - form.Height) / 2));
            form.TopMost = true;
            if (!form.Visible)
            {
                form.Show();
            }

            form.BringToFront();
            ActiveWindowHelper.ForceForeground(form.Handle);
            form.Activate();
        }
    }
}
