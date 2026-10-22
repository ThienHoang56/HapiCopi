using System.Drawing;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Điều khiển hiển thị cửa sổ soạn thảo: đưa cửa sổ vào giữa màn hình làm việc và kích hoạt focus.
    /// </summary>
    internal static class EditorWindow
    {

        public static void Present(Form form)
        {
            form.ShowInTaskbar = false;
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
