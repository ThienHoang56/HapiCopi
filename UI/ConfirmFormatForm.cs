using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HapiCopi
{
    public class ConfirmFormatForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private readonly string _originalText;
        private readonly string _suggestedText;
        private readonly string _evaluation;
        private bool _isApplied = false;

        public bool IsApplied => _isApplied;

        public ConfirmFormatForm(string originalText, string suggestedText, string evaluation)
        {
            _originalText = originalText;
            _suggestedText = suggestedText;
            _evaluation = evaluation;
            this.ResizeRedraw = true;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(900, 620);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(30, 30, 30); // Dark theme base
            this.Load += (s, e) =>
            {
                this.MaximizedBounds = Screen.FromHandle(this.Handle).WorkingArea;
            };

            // Enable resizing border or drop shadow if needed, here we draw a subtle border
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(63, 63, 70), 2))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            // 1. Title bar custom drag panel
            var titlePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(45, 45, 48)
            };
            titlePanel.MouseDown += TitlePanel_MouseDown;

            var titleLabel = new Label
            {
                Text = "HapiCopi - AI Suggested Reformatting",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(12, 10),
                AutoSize = true
            };
            titlePanel.Controls.Add(titleLabel);

            var minButton = new Button
            {
                Text = "—",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Width = 40,
                Height = 40,
                Location = new Point(this.Width - 120, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            minButton.FlatAppearance.BorderSize = 0;
            minButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            minButton.Click += (s, e) =>
            {
                this.WindowState = FormWindowState.Minimized;
            };
            titlePanel.Controls.Add(minButton);

            var maxButton = new Button
            {
                Text = "🗖",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Width = 40,
                Height = 40,
                Location = new Point(this.Width - 80, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            maxButton.FlatAppearance.BorderSize = 0;
            maxButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            maxButton.Click += (s, e) =>
            {
                if (this.WindowState == FormWindowState.Maximized)
                {
                    this.WindowState = FormWindowState.Normal;
                    maxButton.Text = "🗖";
                }
                else
                {
                    this.WindowState = FormWindowState.Maximized;
                    maxButton.Text = "🗗";
                }
            };
            titlePanel.Controls.Add(maxButton);

            var closeButton = new Button
            {
                Text = "✕",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Width = 40,
                Height = 40,
                Location = new Point(this.Width - 40, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(231, 76, 60);
            closeButton.Click += (s, e) => this.Close();
            titlePanel.Controls.Add(closeButton);

            // titlePanel will be added at the end of the method

            // 2. AI Comment Panel (gợi ý của AI)
            var commentPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 85,
                BackColor = Color.FromArgb(38, 38, 38),
                Padding = new Padding(12)
            };

            // Custom border / accent line for comment box
            commentPanel.Paint += (s, e) =>
            {
                using (var brush = new SolidBrush(Color.FromArgb(52, 152, 219)))
                {
                    e.Graphics.FillRectangle(brush, 0, 0, 4, commentPanel.Height);
                }
            };

            var commentIcon = new Label
            {
                Text = "💡 Nhận xét ngữ nghĩa từ Gemini AI:",
                ForeColor = Color.FromArgb(52, 152, 219),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(16, 10),
                AutoSize = true
            };
            commentPanel.Controls.Add(commentIcon);

            var commentText = new Label
            {
                Text = string.IsNullOrEmpty(_evaluation) ? "AI không phát hiện lỗi ngữ pháp hay phong cách hành văn nào cần chỉnh sửa sâu, chỉ đề xuất định dạng sạch sẽ." : _evaluation,
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                Location = new Point(16, 30),
                Size = new Size(this.Width - 60, 45),
                UseCompatibleTextRendering = true
            };
            commentPanel.Controls.Add(commentText);

            var hideCommentButton = new Button
            {
                Text = "✕",
                ForeColor = Color.FromArgb(150, 150, 150),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Width = 24,
                Height = 24,
                Location = new Point(this.Width - 36, 8),
                Cursor = Cursors.Hand
            };
            hideCommentButton.FlatAppearance.BorderSize = 0;
            hideCommentButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            hideCommentButton.Click += (s, e) => commentPanel.Visible = false;
            commentPanel.Controls.Add(hideCommentButton);

            // 3. Bottom Action Panel
            var actionPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.FromArgb(30, 30, 30)
            };

            var applyButton = new Button
            {
                Text = "✓ Áp dụng (Replace Text)",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(46, 204, 113),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Width = 200,
                Height = 36,
                Location = new Point(this.Width - 420, 12),
                Cursor = Cursors.Hand
            };
            applyButton.FlatAppearance.BorderSize = 0;
            applyButton.Click += (s, e) =>
            {
                _isApplied = true;
                this.Close();
            };
            actionPanel.Controls.Add(applyButton);

            var cancelButton = new Button
            {
                Text = "✕ Bỏ qua (Keep Original)",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(231, 76, 60),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Width = 180,
                Height = 36,
                Location = new Point(this.Width - 200, 12),
                Cursor = Cursors.Hand
            };
            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.Click += (s, e) => this.Close();
            actionPanel.Controls.Add(cancelButton);

            // actionPanel will be added at the end of the method

            // 4. Middle Content Split Panel (Văn bản gốc vs Văn bản gợi ý)
            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 10, 12, 10)
            };

            var tableLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // Left: Original Text Box
            var originalGroup = new GroupBox
            {
                Text = "VĂN BẢN GỐC",
                ForeColor = Color.FromArgb(231, 76, 60),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 6, 0)
            };

            var originalBox = new TextBox
            {
                Text = _originalText,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };
            // Remove text margin issues
            originalGroup.Controls.Add(originalBox);

            // Right: Suggested Text Box
            var suggestedGroup = new GroupBox
            {
                Text = "GỢI Ý SAU KHI ĐỊNH DẠNG (AI)",
                ForeColor = Color.FromArgb(46, 204, 113),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 0, 0, 0)
            };

            var suggestedBox = new TextBox
            {
                Text = _suggestedText,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };
            suggestedGroup.Controls.Add(suggestedBox);

            tableLayout.Controls.Add(originalGroup, 0, 0);
            tableLayout.Controls.Add(suggestedGroup, 1, 0);

            contentPanel.Controls.Add(tableLayout);

            // Add panels in reverse docking order to ensure proper layout and no overlapping!
            this.Controls.Add(contentPanel);
            this.Controls.Add(actionPanel);
            this.Controls.Add(commentPanel);
            this.Controls.Add(titlePanel);
        }

        private void TitlePanel_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }
    }
}
