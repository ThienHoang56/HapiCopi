using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Micro-Widget tiến trình dễ thương (Cute Magic Wand Indicator - 100% không dùng chữ).
    /// Hiển thị cạnh con trỏ chuột khi kích hoạt "Dán thông minh" (Ctrl+Shift+V),
    /// chạy hoạt ảnh đũa phép/ngôi sao lấp lánh (sparkles) trong ~300ms, nở thành tick xanh rồi tự biến mất.
    /// Hoàn toàn không chiếm focus (WS_EX_NOACTIVATE, WS_EX_TRANSPARENT), không làm gián đoạn người dùng.
    /// </summary>
    public sealed class MagicWandIndicatorForm : Form
    {
        private readonly System.Windows.Forms.Timer _animTimer;
        private float _animPhase = 0f;
        private bool _isSuccess = false;
        private bool _isDeactivating = false;
        private float _successScale = 0f;
        private float _fadeOpacity = 1f;
        private int _tickCount = 0;

        private static MagicWandIndicatorForm? _instance;

        public MagicWandIndicatorForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(38, 38);
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;

            _animTimer = new System.Windows.Forms.Timer
            {
                Interval = 16 // ~60 FPS
            };
            _animTimer.Tick += OnAnimTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: Tuyệt đối không cướp focus
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: Không hiện trong Alt+Tab
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT: Chuột click xuyên qua không bị cản
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        /// <summary>
        /// Hiển thị widget phép thuật tại vị trí con trỏ chuột.
        /// </summary>
        public static void ShowNearCursor(Point cursorPosition)
        {
            try
            {
                if (_instance == null || _instance.IsDisposed)
                {
                    _instance = new MagicWandIndicatorForm();
                }

                _instance.ResetState();

                // Đặt vị trí lệch nhẹ 14px sang phải và 18px lên trên con trỏ chuột
                var screen = Screen.FromPoint(cursorPosition).WorkingArea;
                int targetX = Math.Clamp(cursorPosition.X + 14, screen.Left + 4, screen.Right - _instance.Width - 4);
                int targetY = Math.Clamp(cursorPosition.Y - 24, screen.Top + 4, screen.Bottom - _instance.Height - 4);

                _instance.Location = new Point(targetX, targetY);
                _instance.Opacity = 1.0;
                _instance.Show();
                _instance._animTimer.Start();
            }
            catch (Exception ex)
            {
                Program.Log($"[MagicWand] Lỗi hiển thị: {ex.Message}");
            }
        }

        /// <summary>
        /// Đánh dấu đã xử lý thành công, chuyển sang hoạt ảnh bung nở (pop) rồi mờ dần.
        /// </summary>
        public static void NotifySuccess()
        {
            try
            {
                if (_instance != null && !_instance.IsDisposed && _instance.Visible)
                {
                    _instance._isSuccess = true;
                    _instance._isDeactivating = false;
                }
            }
            catch { }
        }

        /// <summary>
        /// Đánh dấu tắt chế độ Magic Wand, chuyển sang hoạt ảnh mờ dần nhanh.
        /// </summary>
        public static void NotifyDeactivated()
        {
            try
            {
                if (_instance != null && !_instance.IsDisposed && _instance.Visible)
                {
                    _instance._isDeactivating = true;
                }
                else
                {
                    ShowNearCursor(Cursor.Position);
                    if (_instance != null)
                    {
                        _instance._isDeactivating = true;
                    }
                }
            }
            catch { }
        }

        private void ResetState()
        {
            _animPhase = 0f;
            _isSuccess = false;
            _isDeactivating = false;
            _successScale = 0f;
            _fadeOpacity = 1f;
            _tickCount = 0;
            Opacity = 1.0;
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            _tickCount++;
            _animPhase += 0.12f;

            if (_isDeactivating)
            {
                _fadeOpacity -= 0.14f;
                if (_fadeOpacity <= 0f)
                {
                    _animTimer.Stop();
                    Hide();
                    return;
                }
                Opacity = Math.Max(0.0, _fadeOpacity);
            }
            else if (_isSuccess)
            {
                _successScale = Math.Min(1.2f, _successScale + 0.18f);
                if (_tickCount > 18) // Sau khoảng 280ms
                {
                    _fadeOpacity -= 0.12f;
                    if (_fadeOpacity <= 0f)
                    {
                        _animTimer.Stop();
                        Hide();
                        return;
                    }
                    Opacity = Math.Max(0.0, _fadeOpacity);
                }
            }
            else if (_tickCount > 120) // Sau 2 giây nếu đang scan quá lâu thì tự ẩn
            {
                _animTimer.Stop();
                Hide();
                return;
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int w = ClientSize.Width;
            int h = ClientSize.Height;
            float cx = w / 2f;
            float cy = h / 2f;

            // 1. Vẽ vòng tròn nền kẹo dẻo (Jelly pill background)
            float bgScale = _isSuccess ? (1.0f + (_successScale - 1.0f) * 0.4f) : (1.0f + (float)Math.Sin(_animPhase * 2) * 0.05f);
            float radius = 16f * bgScale;

            Color c1, c2;
            if (_isDeactivating)
            {
                c1 = Color.FromArgb(100, 116, 139); // Slate grey
                c2 = Color.FromArgb(71, 85, 105);
            }
            else if (_isSuccess)
            {
                c1 = Color.FromArgb(16, 185, 129); // Emerald green
                c2 = Color.FromArgb(52, 211, 153);
            }
            else
            {
                c1 = Color.FromArgb(14, 124, 134); // Teal brand
                c2 = Color.FromArgb(20, 184, 166);
            }

            using (var brush = new LinearGradientBrush(
                new PointF(cx - radius, cy - radius),
                new PointF(cx + radius, cy + radius),
                c1, c2))
            {
                g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            // Viền sáng mềm mại
            using (var borderPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f))
            {
                g.DrawEllipse(borderPen, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            if (_isDeactivating)
            {
                // 1b. Vẽ dấu gạch ngang trắng nhẹ nhàng biểu thị tắt chế độ
                using var minusPen = new Pen(Color.White, 2.2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                g.DrawLine(minusPen, cx - 5f, cy, cx + 5f, cy);
            }
            else if (_isSuccess)
            {
                // 2. Vẽ dấu tick trắng tròn xinh xắn khi thành công
                using var tickPen = new Pen(Color.White, 2.4f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                g.DrawLines(tickPen, new[]
                {
                    new PointF(cx - 5.5f, cy),
                    new PointF(cx - 1.5f, cy + 4f),
                    new PointF(cx + 6f, cy - 4.5f)
                });
            }
            else
            {
                // 3. Vẽ các tia sao lấp lánh (Sparkles ✨) quay nhẹ nhàng
                float rotAngle = _animPhase * 35f;

                // Ngôi sao lớn ở giữa
                DrawSparkleStar(g, cx, cy, size: 7f + (float)Math.Sin(_animPhase * 3) * 1.5f, angle: rotAngle, Color.White);

                // Ngôi sao nhỏ phía trên bên phải
                float orbit1Angle = rotAngle * 1.5f * (float)(Math.PI / 180.0);
                float s1x = cx + (float)Math.Cos(orbit1Angle) * 8.5f;
                float s1y = cy + (float)Math.Sin(orbit1Angle) * 8.5f;
                DrawSparkleStar(g, s1x, s1y, size: 3.5f, angle: -rotAngle * 2f, Color.FromArgb(254, 240, 138));

                // Ngôi sao nhỏ phía dưới bên trái
                float s2x = cx - (float)Math.Cos(orbit1Angle) * 7.5f;
                float s2y = cy - (float)Math.Sin(orbit1Angle) * 7.5f;
                DrawSparkleStar(g, s2x, s2y, size: 2.8f, angle: rotAngle * 2f, Color.FromArgb(255, 255, 255));
            }
        }

        private static void DrawSparkleStar(Graphics g, float cx, float cy, float size, float angle, Color color)
        {
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);

            using var brush = new SolidBrush(color);
            using var path = new GraphicsPath();

            // Vẽ hình sao 4 cánh mềm mại (diamond star ✨)
            float s = size;
            float inner = s * 0.22f;

            path.AddPolygon(new[]
            {
                new PointF(0, -s),
                new PointF(inner, -inner),
                new PointF(s, 0),
                new PointF(inner, inner),
                new PointF(0, s),
                new PointF(-inner, inner),
                new PointF(-s, 0),
                new PointF(-inner, -inner)
            });

            g.FillPath(brush, path);
            g.Restore(state);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
