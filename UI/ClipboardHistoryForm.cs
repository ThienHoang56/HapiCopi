using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Một mục trong lịch sử clipboard.
    /// </summary>
    public class ClipboardHistoryItem
    {
        public string Id { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string AppName { get; set; } = "Unknown";
        public string OriginalText { get; set; } = "";
        public string OriginalHtml { get; set; } = "";
        public string ProcessedText { get; set; } = "";
        public string ProcessedHtml { get; set; } = "";
        public string Status { get; set; } = "Bỏ qua";  // "Đã xử lý", "Bỏ qua", "PDF Heuristic"
        public PiiScanResult? PiiResult { get; set; }

        /// <summary>
        /// Preview text ngắn gọn cho sidebar.
        /// </summary>
        public string PreviewText
        {
            get
            {
                string src = !string.IsNullOrEmpty(OriginalText) ? OriginalText : "(Không có text)";
                src = src.Replace("\r", "").Replace("\n", " ").Trim();
                return src.Length > 60 ? src.Substring(0, 60) + "..." : src;
            }
        }
    }

    /// <summary>
    /// Cửa sổ hiển thị lịch sử clipboard với khả năng xem chi tiết nội dung gốc/sau xử lý,
    /// che thông tin nhạy cảm (PII masking), và các thao tác sao chép/xóa.
    /// </summary>
    public class ClipboardHistoryForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private readonly List<ClipboardHistoryItem> _historySource;
        private readonly ListBox _historyList;
        private readonly TextBox _originalBox;
        private readonly TextBox _processedBox;
        private readonly Label _statusLabel;
        private readonly Label _piiWarningLabel;
        private readonly CheckBox _showRawCheckbox;
        private bool _showUnmasked = false;

        public ClipboardHistoryForm(List<ClipboardHistoryItem> history)
        {
            _historySource = history;
            this.Text = "HapiCopi - Lịch sử Clipboard";
            this.Size = new Size(960, 620);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;

            // Border
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(63, 63, 70), 2))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            // ============================
            // TITLE BAR
            // ============================
            var titlePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(45, 45, 48)
            };
            titlePanel.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { try { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); } catch { } }
            };

            var titleLabel = new Label
            {
                Text = "🛡️ HapiCopi — Lịch sử Clipboard & Bảo mật",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(12, 10),
                AutoSize = true
            };
            titlePanel.Controls.Add(titleLabel);

            var closeButton = new Button
            {
                Text = "✕",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Width = 40, Height = 40,
                Location = new Point(this.Width - 40, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(231, 76, 60);
            closeButton.Click += (s, e) => this.Close();
            titlePanel.Controls.Add(closeButton);

            // ============================
            // SIDEBAR (Danh sách sự kiện)
            // ============================
            var sidebarPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 280,
                BackColor = Color.FromArgb(35, 35, 38),
                Padding = new Padding(8, 8, 8, 8)
            };

            var sidebarTitle = new Label
            {
                Text = "Sự kiện gần đây",
                ForeColor = Color.FromArgb(46, 204, 113),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(4, 4, 0, 0)
            };
            sidebarPanel.Controls.Add(sidebarTitle);

            _historyList = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI", 9),
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 52
            };
            _historyList.DrawItem += HistoryList_DrawItem;
            _historyList.SelectedIndexChanged += HistoryList_SelectedIndexChanged;
            sidebarPanel.Controls.Add(_historyList);

            // Nút xóa lịch sử
            var clearButton = new Button
            {
                Text = "🗑 Xóa lịch sử",
                ForeColor = Color.FromArgb(180, 180, 180),
                BackColor = Color.FromArgb(45, 45, 48),
                FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Bottom,
                Height = 32,
                Cursor = Cursors.Hand
            };
            clearButton.FlatAppearance.BorderSize = 0;
            clearButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(231, 76, 60);
            clearButton.Click += (s, e) =>
            {
                _historySource.Clear();
                RefreshHistoryList();
                _originalBox.Text = "";
                _processedBox.Text = "";
                _statusLabel.Text = "";
                _piiWarningLabel.Text = "";
            };
            sidebarPanel.Controls.Add(clearButton);

            // ============================
            // CONTENT PANEL (Chi tiết)
            // ============================
            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8)
            };

            // Status bar
            var statusPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(30, 30, 30)
            };

            _statusLabel = new Label
            {
                Text = "Chọn một mục bên trái để xem chi tiết",
                ForeColor = Color.FromArgb(140, 140, 140),
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Location = new Point(0, 6)
            };
            statusPanel.Controls.Add(_statusLabel);

            contentPanel.Controls.Add(statusPanel);

            // PII Warning label
            _piiWarningLabel = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(230, 126, 34),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 0,
                Padding = new Padding(0, 2, 0, 2)
            };
            contentPanel.Controls.Add(_piiWarningLabel);

            // Action bar
            var actionBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = Color.FromArgb(30, 30, 30)
            };

            var copyOriginalBtn = new Button
            {
                Text = "📋 Sao chép bản gốc",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(52, 152, 219),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Width = 155, Height = 30,
                Location = new Point(0, 5),
                Cursor = Cursors.Hand
            };
            copyOriginalBtn.FlatAppearance.BorderSize = 0;
            copyOriginalBtn.Click += (s, e) => CopySelectedToClipboard(original: true);
            actionBar.Controls.Add(copyOriginalBtn);

            var copyProcessedBtn = new Button
            {
                Text = "📋 Sao chép bản mới",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(46, 204, 113),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Width = 155, Height = 30,
                Location = new Point(165, 5),
                Cursor = Cursors.Hand
            };
            copyProcessedBtn.FlatAppearance.BorderSize = 0;
            copyProcessedBtn.Click += (s, e) => CopySelectedToClipboard(original: false);
            actionBar.Controls.Add(copyProcessedBtn);

            _showRawCheckbox = new CheckBox
            {
                Text = "👁 Hiện thông tin gốc (bỏ che)",
                ForeColor = Color.FromArgb(160, 160, 160),
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Location = new Point(340, 9),
                Checked = false
            };
            _showRawCheckbox.CheckedChanged += (s, e) =>
            {
                _showUnmasked = _showRawCheckbox.Checked;
                RefreshDetailView();
            };
            actionBar.Controls.Add(_showRawCheckbox);

            contentPanel.Controls.Add(actionBar);

            // Text boxes (side-by-side)
            var textPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            textPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            textPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var originalGroup = new GroupBox
            {
                Text = "NỘI DUNG GỐC (KHI COPY)",
                ForeColor = Color.FromArgb(231, 76, 60),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 4, 0)
            };

            _originalBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill
            };
            originalGroup.Controls.Add(_originalBox);

            var processedGroup = new GroupBox
            {
                Text = "SAU KHI XỬ LÝ",
                ForeColor = Color.FromArgb(46, 204, 113),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 0, 0, 0)
            };

            _processedBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill
            };
            processedGroup.Controls.Add(_processedBox);

            textPanel.Controls.Add(originalGroup, 0, 0);
            textPanel.Controls.Add(processedGroup, 1, 0);

            contentPanel.Controls.Add(textPanel);

            // ============================
            // ASSEMBLY (thứ tự ngược cho DockStyle)
            // ============================
            this.Controls.Add(contentPanel);
            this.Controls.Add(sidebarPanel);
            this.Controls.Add(titlePanel);

            // Load data
            RefreshHistoryList();
        }

        // ============================
        // RENDER SIDEBAR ITEMS
        // ============================

        private void HistoryList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _historyList.Items.Count) return;

            var item = _historyList.Items[e.Index] as ClipboardHistoryItem;
            if (item == null) return;

            bool isSelected = (e.State & DrawItemState.Selected) != 0;
            Color bgColor = isSelected ? Color.FromArgb(50, 50, 55) : Color.FromArgb(28, 28, 28);

            using (var bg = new SolidBrush(bgColor))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            // Accent line bên trái
            Color accentColor = item.PiiResult?.HasPii == true
                ? Color.FromArgb(230, 126, 34) // Orange cho PII
                : item.Status == "Đã xử lý"
                    ? Color.FromArgb(46, 204, 113) // Green cho đã xử lý
                    : Color.FromArgb(80, 80, 80);   // Grey cho bỏ qua

            using (var accentBrush = new SolidBrush(accentColor))
            {
                e.Graphics.FillRectangle(accentBrush, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            }

            // Timestamp + App
            string timeApp = $"{item.Timestamp:HH:mm:ss} — {item.AppName}";
            using (var font = new Font("Segoe UI", 7.5f))
            using (var brush = new SolidBrush(Color.FromArgb(120, 120, 120)))
            {
                e.Graphics.DrawString(timeApp, font, brush, e.Bounds.X + 10, e.Bounds.Y + 4);
            }

            // Preview
            using (var font = new Font("Segoe UI", 8.5f))
            using (var brush = new SolidBrush(Color.FromArgb(200, 200, 200)))
            {
                e.Graphics.DrawString(item.PreviewText, font, brush,
                    new RectangleF(e.Bounds.X + 10, e.Bounds.Y + 20, e.Bounds.Width - 20, 28));
            }

            // PII badge
            if (item.PiiResult?.HasPii == true)
            {
                string badge = item.PiiResult.HasCriticalPii ? "⚠ CRITICAL" : "⚠ PII";
                using (var font = new Font("Segoe UI", 7f, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.FromArgb(230, 126, 34)))
                {
                    var textSize = e.Graphics.MeasureString(badge, font);
                    e.Graphics.DrawString(badge, font, brush,
                        e.Bounds.Right - textSize.Width - 8, e.Bounds.Y + 4);
                }
            }

            // Bottom separator
            using (var pen = new Pen(Color.FromArgb(45, 45, 48)))
            {
                e.Graphics.DrawLine(pen, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        }

        private void HistoryList_SelectedIndexChanged(object? sender, EventArgs e)
        {
            RefreshDetailView();
        }

        // ============================
        // REFRESH UI
        // ============================

        public void RefreshHistoryList()
        {
            _historyList.Items.Clear();
            // Hiển thị mới nhất lên đầu
            foreach (var item in _historySource.AsEnumerable().Reverse())
            {
                _historyList.Items.Add(item);
            }
            if (_historyList.Items.Count > 0)
            {
                _historyList.SelectedIndex = 0;
            }
        }

        private void RefreshDetailView()
        {
            if (_historyList.SelectedItem is not ClipboardHistoryItem item) return;

            // Status
            _statusLabel.Text = $"{item.Timestamp:yyyy-MM-dd HH:mm:ss}  |  Nguồn: {item.AppName}  |  Trạng thái: {item.Status}";

            // PII Warning
            if (item.PiiResult?.HasPii == true)
            {
                _piiWarningLabel.Text = $"⚠ Phát hiện: {item.PiiResult.GetSummary()} ({item.PiiResult.Matches.Count} mục)";
                _piiWarningLabel.Height = 24;
            }
            else
            {
                _piiWarningLabel.Text = "";
                _piiWarningLabel.Height = 0;
            }

            // Nội dung
            if (_showUnmasked || item.PiiResult == null || !item.PiiResult.HasPii)
            {
                _originalBox.Text = item.OriginalText;
            }
            else
            {
                _originalBox.Text = item.PiiResult.GetSanitizedText(item.OriginalText);
            }

            _processedBox.Text = !string.IsNullOrEmpty(item.ProcessedText) ? item.ProcessedText : "(Không thay đổi)";
        }

        private void CopySelectedToClipboard(bool original)
        {
            if (_historyList.SelectedItem is not ClipboardHistoryItem item) return;

            try
            {
                string textToCopy = original ? item.OriginalText : item.ProcessedText;
                if (string.IsNullOrEmpty(textToCopy)) textToCopy = item.OriginalText;

                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy);
                    _statusLabel.Text = original ? "✅ Đã sao chép bản gốc vào Clipboard" : "✅ Đã sao chép bản đã xử lý vào Clipboard";
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[History] Lỗi copy: {ex.Message}");
            }
        }
    }
}
