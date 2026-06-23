using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HapiCopi
{
    public class FormatProgressForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private int _angle = 0;
        private readonly System.Windows.Forms.Timer _animationTimer;
        private readonly Label _mainLabel;
        private readonly Label _subLabel;
        private readonly System.Threading.CancellationTokenSource? _cts;

        protected override bool ShowWithoutActivation => true;

        public FormatProgressForm(System.Threading.CancellationTokenSource? cts = null)
        {
            _cts = cts;
            this.Size = new Size(450, 100);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(28, 28, 28);
            this.ShowInTaskbar = false;
            this.TopMost = true;

            // Make the form draggable from anywhere on the body
            this.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragForm(); };

            _mainLabel = new Label
            {
                Text = "HapiCopi đang xử lý...",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Location = new Point(85, 26),
                AutoSize = true
            };
            _mainLabel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragForm(); };
            this.Controls.Add(_mainLabel);

            _subLabel = new Label
            {
                Text = "Đang tối ưu hóa cấu trúc và ngữ nghĩa văn bản...",
                ForeColor = Color.FromArgb(160, 160, 160),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Location = new Point(85, 50),
                Size = new Size(320, 30)
            };
            _subLabel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DragForm(); };
            this.Controls.Add(_subLabel);

            // Close button (Cancel) for the loading form
            var cancelButton = new Button
            {
                Text = "✕",
                ForeColor = Color.FromArgb(180, 180, 180),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Width = 24,
                Height = 24,
                Location = new Point(this.Width - 30, 6),
                Cursor = Cursors.Hand
            };
            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(231, 76, 60);
            cancelButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(192, 57, 43);
            cancelButton.Click += (s, e) =>
            {
                Program.Log("Người dùng hủy yêu cầu định dạng bằng nút ✕ trên thanh progress bar.");
                _cts?.Cancel();
                this.Close();
            };
            this.Controls.Add(cancelButton);

            // Subtle accent stripe on the left border
            this.Paint += (s, e) =>
            {
                // Draw card border
                using (var pen = new Pen(Color.FromArgb(63, 63, 70), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }

                // Draw solid primary color stripe on the left edge
                using (var brush = new SolidBrush(Color.FromArgb(52, 152, 219)))
                {
                    e.Graphics.FillRectangle(brush, 0, 0, 4, this.Height);
                }

                // Draw loading spinner background circle
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int spinnerSize = 36;
                int x = 28;
                int y = (this.Height - spinnerSize) / 2;

                using (var bgPen = new Pen(Color.FromArgb(45, 45, 48), 3.5f))
                {
                    e.Graphics.DrawEllipse(bgPen, x, y, spinnerSize, spinnerSize);
                }

                // Draw active spinning arc
                using (var arcPen = new Pen(Color.FromArgb(52, 152, 219), 3.5f))
                {
                    e.Graphics.DrawArc(arcPen, x, y, spinnerSize, spinnerSize, _angle, 100);
                }
            };

            _animationTimer = new System.Windows.Forms.Timer
            {
                Interval = 15
            };
            _animationTimer.Tick += (s, e) =>
            {
                _angle = (_angle + 8) % 360;
                this.Invalidate(new Rectangle(20, 20, 50, 60)); // Only repaint the spinner region to avoid flicker
            };
            
            this.Load += (s, e) => _animationTimer.Start();
            this.FormClosing += (s, e) => _animationTimer.Stop();
        }

        private void DragForm()
        {
            try
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
            catch (Exception ex)
            {
                Program.Log($"[Drag Lỗi] {ex.Message}");
            }
        }

        public void UpdateStatus(string mainText, string subText)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateStatus(mainText, subText)));
                return;
            }
            _mainLabel.Text = mainText;
            _subLabel.Text = subText;
        }
    }
}
