using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Toast notification tùy biến hiển thị ở góc dưới bên phải màn hình.
    /// Không cướp focus ứng dụng đang hoạt động.
    /// Tự động biến mất sau khoảng thời gian chỉ định.
    /// </summary>
    public class PiiToastForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private readonly System.Windows.Forms.Timer _autoCloseTimer;
        private readonly System.Windows.Forms.Timer _fadeTimer;
        private readonly Label _titleLabel;
        private readonly Label _detailLabel;
        private double _currentOpacity = 0.95;
        private bool _isFadingOut = false;

        /// <summary>
        /// Được gọi khi người dùng nhấn "Mã hóa" trên toast.
        /// </summary>
        public event Action? OnSanitizeClicked;

        /// <summary>
        /// Được gọi khi người dùng nhấn "Giữ nguyên" trên toast.
        /// </summary>
        public event Action? OnIgnoreClicked;

        /// <summary>
        /// Ngăn form chiếm focus khi hiển thị.
        /// </summary>
        protected override bool ShowWithoutActivation => true;

        /// <summary>
        /// Ngăn form xuất hiện trên taskbar và hiện như tool window.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW — không hiển thị trên taskbar
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                return cp;
            }
        }

        public PiiToastForm(string piiSummary, int autoCloseSeconds = 6, bool isSecurityFirst = false)
        {
            // --- Form Properties ---
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(380, 110);
            this.BackColor = Color.FromArgb(25, 25, 25);
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Opacity = 0.95;
            this.DoubleBuffered = true;

            // Vị trí: góc dưới bên phải màn hình
            var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            this.Location = new Point(workingArea.Right - this.Width - 16, workingArea.Bottom - this.Height - 16);

            // Kéo form bằng chuột
            this.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    try { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); } catch { }
                }
            };

            // --- Custom Paint (viền + accent bar) ---
            this.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                // Viền ngoài
                using (var pen = new Pen(Color.FromArgb(60, 60, 65), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }

                // Accent bar bên trái (màu xanh bảo vệ hoặc màu cam cảnh báo)
                Color accentColor = isSecurityFirst ? Color.FromArgb(46, 204, 113) : Color.FromArgb(230, 126, 34);
                using (var brush = new SolidBrush(accentColor))
                {
                    e.Graphics.FillRectangle(brush, 0, 0, 4, this.Height);
                }

                // Icon cảnh báo
                using (var font = new Font("Segoe UI Emoji", 18))
                {
                    e.Graphics.DrawString("🛡️", font, Brushes.White, new Point(14, 14));
                }
            };

            // --- Title Label ---
            _titleLabel = new Label
            {
                Text = isSecurityFirst ? "🛡️ Đã che thông tin nhạy cảm" : "⚠ Phát hiện thông tin nhạy cảm",
                ForeColor = isSecurityFirst ? Color.FromArgb(46, 204, 113) : Color.FromArgb(230, 126, 34),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(52, 14),
                AutoSize = true
            };
            _titleLabel.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    try { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); } catch { }
                }
            };
            this.Controls.Add(_titleLabel);

            // --- Detail Label ---
            _detailLabel = new Label
            {
                Text = TruncateText(piiSummary, 55),
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(52, 38),
                Size = new Size(310, 18)
            };
            this.Controls.Add(_detailLabel);

            // --- Buttons ---
            var sanitizeButton = new Button
            {
                Text = isSecurityFirst ? "Đồng ý 🛡️" : "🔒 Mã hóa",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(46, 204, 113),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Width = 100,
                Height = 30,
                Location = new Point(52, 66),
                Cursor = Cursors.Hand
            };
            sanitizeButton.FlatAppearance.BorderSize = 0;
            sanitizeButton.Click += (s, e) =>
            {
                OnSanitizeClicked?.Invoke();
                FadeOutAndClose();
            };
            this.Controls.Add(sanitizeButton);

            var ignoreButton = new Button
            {
                Text = isSecurityFirst ? "Khôi phục" : "Giữ nguyên",
                ForeColor = Color.FromArgb(160, 160, 160),
                BackColor = Color.FromArgb(45, 45, 48),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f),
                Width = 100,
                Height = 30,
                Location = new Point(160, 66),
                Cursor = Cursors.Hand
            };
            ignoreButton.FlatAppearance.BorderSize = 0;
            ignoreButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 65);
            ignoreButton.Click += (s, e) =>
            {
                OnIgnoreClicked?.Invoke();
                FadeOutAndClose();
            };
            this.Controls.Add(ignoreButton);

            // Nút đóng
            var closeButton = new Button
            {
                Text = "✕",
                ForeColor = Color.FromArgb(130, 130, 130),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Width = 24,
                Height = 24,
                Location = new Point(this.Width - 28, 4),
                Cursor = Cursors.Hand
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(231, 76, 60);
            closeButton.Click += (s, e) => FadeOutAndClose();
            this.Controls.Add(closeButton);

            // --- Auto-close Timer ---
            _autoCloseTimer = new System.Windows.Forms.Timer { Interval = autoCloseSeconds * 1000 };
            _autoCloseTimer.Tick += (s, e) =>
            {
                _autoCloseTimer.Stop();
                FadeOutAndClose();
            };

            // --- Fade Timer ---
            _fadeTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _fadeTimer.Tick += (s, e) =>
            {
                _currentOpacity -= 0.08;
                if (_currentOpacity <= 0)
                {
                    _fadeTimer.Stop();
                    this.Close();
                }
                else
                {
                    this.Opacity = _currentOpacity;
                }
            };

            this.Load += (s, e) => _autoCloseTimer.Start();
            this.FormClosing += (s, e) =>
            {
                _autoCloseTimer.Stop();
                _fadeTimer.Stop();
            };
        }

        private void FadeOutAndClose()
        {
            if (_isFadingOut) return;
            _isFadingOut = true;
            _autoCloseTimer.Stop();
            _fadeTimer.Start();
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }
    }
}
