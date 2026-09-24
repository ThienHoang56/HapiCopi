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
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string AppName { get; set; } = "Unknown";
        public string OriginalText { get; set; } = "";
        public string OriginalHtml { get; set; } = "";
        public string ProcessedText { get; set; } = "";
        public string ProcessedHtml { get; set; } = "";
        public string Status { get; set; } = "Bỏ qua";  // "Đã xử lý", "Bỏ qua", "PDF Heuristic", "🛡️ Đã che (Mặc định)", "⚠ Đã khôi phục"
        public PiiScanResult? PiiResult { get; set; }
        public Dictionary<string, string> ClipboardFormats { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Preview text ngắn gọn cho sidebar.
        /// </summary>
        public string PreviewText
        {
            get
            {
                string src = !string.IsNullOrEmpty(OriginalText) ? OriginalText : "(Không có text)";
                src = src.Replace("\r", "").Replace("\n", " ").Trim();
                return src.Length > 45 ? src.Substring(0, 45) + "..." : src;
            }
        }
    }

    /// <summary>
    /// Cửa sổ hiển thị lịch sử clipboard với thiết kế tối giản, hiện đại,
    /// hỗ trợ che thông tin nhạy cảm và các thao tác sao chép nhanh.
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
        private readonly RichTextBox _originalBox;
        private readonly RichTextBox _processedBox;
        private readonly Label _statusLabel;
        private readonly Label _piiWarningLabel;
        private readonly Label _securityStatusLabel;
        private readonly CheckBox _showRawCheckbox;
        private bool _showUnmasked = false;

        // Clipboard Inspector Components (Phase 1)
        private readonly TableLayoutPanel _textPanel;
        private readonly Panel _inspectorPanel;
        private readonly ListBox _formatListBox;
        private readonly RichTextBox _formatContentBox;
        private readonly Button _btnPreview;
        private readonly Button _btnInspector;
        private bool _isInInspectorMode = false;

        // Bảng màu thiết kế tối giản (Slate Dark Theme)
        private static readonly Color ColorBgObsidian = Color.FromArgb(18, 18, 20);
        private static readonly Color ColorBgCard = Color.FromArgb(28, 28, 32);
        private static readonly Color ColorBgSidebar = Color.FromArgb(24, 24, 26);
        private static readonly Color ColorTextPrimary = Color.FromArgb(240, 240, 245);
        private static readonly Color ColorTextMuted = Color.FromArgb(145, 145, 155);
        private static readonly Color ColorAccentGreen = Color.FromArgb(46, 204, 113);
        private static readonly Color ColorAccentBlue = Color.FromArgb(52, 152, 219);
        private static readonly Color ColorAccentWarning = Color.FromArgb(230, 126, 34);
        private static readonly Color ColorBorder = Color.FromArgb(45, 45, 50);

        public ClipboardHistoryForm(List<ClipboardHistoryItem> history)
        {
            _historySource = history;
            this.Text = "HapiCopi — Lịch sử Clipboard & Bảo mật";
            this.Size = new Size(1000, 640);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColorBgObsidian;
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;

            // Paint Border ngoài cho Form
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(ColorBorder, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            // ============================
            // 1. TITLE BAR (Thanh tiêu đề tùy biến)
            // ============================
            var titlePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 45,
                BackColor = ColorBgSidebar
            };
            titlePanel.Paint += (s, e) =>
            {
                // Đường gạch dưới title bar
                using (var pen = new Pen(ColorBorder, 1))
                {
                    e.Graphics.DrawLine(pen, 0, titlePanel.Height - 1, titlePanel.Width, titlePanel.Height - 1);
                }
            };
            titlePanel.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { try { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); } catch { } }
            };

            var titleLabel = new Label
            {
                Text = "🛡️  HAPICOPI  —  LỊCH SỬ CLIPBOARD",
                ForeColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(16, 13),
                AutoSize = true
            };
            titleLabel.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { try { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); } catch { } }
            };
            titlePanel.Controls.Add(titleLabel);

            _securityStatusLabel = new Label
            {
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI Semibold", 8.5f),
                Location = new Point(this.Width - 320, 14),
                Width = 270,
                Height = 20,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            titlePanel.Controls.Add(_securityStatusLabel);

            var closeButton = new Button
            {
                Text = "✕",
                ForeColor = ColorTextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Width = 45,
                Height = 45,
                Location = new Point(this.Width - 45, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(231, 76, 60);
            closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(192, 57, 43);
            closeButton.Click += (s, e) => this.Close();
            closeButton.MouseEnter += (s, e) => closeButton.ForeColor = Color.White;
            closeButton.MouseLeave += (s, e) => closeButton.ForeColor = ColorTextMuted;
            titlePanel.Controls.Add(closeButton);

            // ============================
            // 2. SIDEBAR (Danh sách lịch sử)
            // ============================
            var sidebarPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 300,
                BackColor = ColorBgSidebar,
                Padding = new Padding(12, 12, 12, 12)
            };
            sidebarPanel.Paint += (s, e) =>
            {
                // Đường gạch chia dọc giữa sidebar và content
                using (var pen = new Pen(ColorBorder, 1))
                {
                    e.Graphics.DrawLine(pen, sidebarPanel.Width - 1, 0, sidebarPanel.Width - 1, sidebarPanel.Height);
                }
            };

            var sidebarTitle = new Label
            {
                Text = "SỰ KIỆN GẦN ĐÂY",
                ForeColor = ColorAccentGreen,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(4, 2, 0, 0)
            };
            sidebarPanel.Controls.Add(sidebarTitle);

            _historyList = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBgSidebar,
                ForeColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 9),
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 62, // Tăng chiều cao để thông thoáng
                Cursor = Cursors.Hand
            };
            _historyList.DrawItem += HistoryList_DrawItem;
            _historyList.SelectedIndexChanged += HistoryList_SelectedIndexChanged;
            sidebarPanel.Controls.Add(_historyList);

            // Nút xóa lịch sử (Thiết kế phẳng, tinh tế)
            var clearButton = new Button
            {
                Text = "🗑  Xóa lịch sử",
                ForeColor = ColorTextMuted,
                BackColor = Color.FromArgb(32, 32, 35),
                FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Bottom,
                Height = 36,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 8, 0, 0)
            };
            clearButton.FlatAppearance.BorderSize = 1;
            clearButton.FlatAppearance.BorderColor = Color.FromArgb(55, 55, 60);
            clearButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(231, 76, 60);
            clearButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(192, 57, 43);
            clearButton.Click += (s, e) =>
            {
                _historySource.Clear();
                HistoryManager.SaveHistory(_historySource);
                RefreshHistoryList();
                _originalBox.Text = "";
                _processedBox.Text = "";
                _statusLabel.Text = "Chọn một mục bên trái để xem chi tiết";
                _piiWarningLabel.Text = "";
                _piiWarningLabel.Height = 0;
            };
            clearButton.MouseEnter += (s, e) => clearButton.ForeColor = Color.White;
            clearButton.MouseLeave += (s, e) => clearButton.ForeColor = ColorTextMuted;
            
            // Spacer panel cho nút xóa
            var spacer = new Panel { Dock = DockStyle.Bottom, Height = 10 };
            sidebarPanel.Controls.Add(spacer);
            sidebarPanel.Controls.Add(clearButton);

            // ============================
            // 3. CONTENT PANEL (Chi tiết)
            // ============================
            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 16, 20, 16)
            };

            // Status Panel
            var statusPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36 // Slightly taller for the buttons
            };
            _statusLabel = new Label
            {
                Text = "Chọn một mục bên trái để xem chi tiết",
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI Semibold", 8.5f),
                AutoSize = true,
                Location = new Point(0, 8)
            };
            statusPanel.Controls.Add(_statusLabel);

            // Container panel for tab buttons to avoid WinForms anchoring gotchas
            var tabButtonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 240,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            _btnPreview = new Button
            {
                Text = "👁️ Xem trước",
                Width = 110,
                Height = 28,
                Margin = new Padding(0, 4, 10, 0),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnPreview.FlatAppearance.BorderSize = 1;
            _btnPreview.Click += (s, e) => { _isInInspectorMode = false; UpdateTabSelection(); };

            _btnInspector = new Button
            {
                Text = "🔍 Thanh tra",
                Width = 110,
                Height = 28,
                Margin = new Padding(0, 4, 0, 0),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnInspector.FlatAppearance.BorderSize = 1;
            _btnInspector.Click += (s, e) => { _isInInspectorMode = true; UpdateTabSelection(); };

            tabButtonPanel.Controls.Add(_btnPreview);
            tabButtonPanel.Controls.Add(_btnInspector);
            statusPanel.Controls.Add(tabButtonPanel);
            contentPanel.Controls.Add(statusPanel);

            // PII Warning Label (Banner cảnh báo nổi bật khi phát hiện nhạy cảm)
            _piiWarningLabel = new Label
            {
                Text = "",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(211, 47, 47), // Màu đỏ dịu mắt
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 0,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 12, 0),
                Margin = new Padding(0, 0, 0, 12)
            };
            contentPanel.Controls.Add(_piiWarningLabel);

            // Action Bar ở dưới cùng
            var actionBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                Padding = new Padding(0, 8, 0, 0)
            };

            var copyOriginalBtn = new Button
            {
                Text = "📋 Sao chép bản gốc",
                ForeColor = Color.White,
                BackColor = ColorBgCard,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Width = 160,
                Height = 36,
                Location = new Point(0, 10),
                Cursor = Cursors.Hand
            };
            copyOriginalBtn.FlatAppearance.BorderSize = 1;
            copyOriginalBtn.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 65);
            copyOriginalBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 45, 50);
            copyOriginalBtn.Click += (s, e) => CopySelectedToClipboard(original: true);
            actionBar.Controls.Add(copyOriginalBtn);

            var copyProcessedBtn = new Button
            {
                Text = "🛡️ Sao chép bản mới",
                ForeColor = Color.White,
                BackColor = ColorAccentGreen,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Width = 160,
                Height = 36,
                Location = new Point(172, 10),
                Cursor = Cursors.Hand
            };
            copyProcessedBtn.FlatAppearance.BorderSize = 0;
            copyProcessedBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 174, 96);
            copyProcessedBtn.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 130, 76);
            copyProcessedBtn.Click += (s, e) => CopySelectedToClipboard(original: false);
            actionBar.Controls.Add(copyProcessedBtn);

            _showRawCheckbox = new CheckBox
            {
                Text = "👁  Hiện thông tin gốc (bỏ che)",
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI Semibold", 8.5f),
                AutoSize = true,
                Location = new Point(355, 18),
                Checked = false,
                Cursor = Cursors.Hand
            };
            _showRawCheckbox.CheckedChanged += (s, e) =>
            {
                _showUnmasked = _showRawCheckbox.Checked;
                RefreshDetailView();
            };
            actionBar.Controls.Add(_showRawCheckbox);
            contentPanel.Controls.Add(actionBar);

            // Khối văn bản hiển thị song song (Side-by-side) dạng Card hiện đại
            _textPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            _textPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _textPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // Card 1: Bản gốc
            var originalCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBgCard,
                Padding = new Padding(16, 12, 16, 16),
                Margin = new Padding(0, 0, 8, 0)
            };
            originalCard.Paint += (s, e) => DrawCardBorder(originalCard, e.Graphics, ColorAccentWarning);

            var originalTitle = new Label
            {
                Text = "NỘI DUNG GỐC (KHI COPY)",
                ForeColor = ColorAccentWarning,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 22
            };
            _originalBox = new RichTextBox
            {
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                ReadOnly = true,
                BackColor = ColorBgCard,
                ForeColor = Color.FromArgb(210, 210, 215),
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill
            };
            originalCard.Controls.Add(_originalBox);
            originalCard.Controls.Add(originalTitle);

            // Card 2: Bản xử lý
            var processedCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBgCard,
                Padding = new Padding(16, 12, 16, 16),
                Margin = new Padding(8, 0, 0, 0)
            };
            processedCard.Paint += (s, e) => DrawCardBorder(processedCard, e.Graphics, ColorAccentGreen);

            var processedTitle = new Label
            {
                Text = "NỘI DUNG SAU XỬ LÝ (BẢN MỚI)",
                ForeColor = ColorAccentGreen,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 22
            };
            _processedBox = new RichTextBox
            {
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                ReadOnly = true,
                BackColor = ColorBgCard,
                ForeColor = ColorTextPrimary,
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill
            };
            processedCard.Controls.Add(_processedBox);
            processedCard.Controls.Add(processedTitle);

            _textPanel.Controls.Add(originalCard, 0, 0);
            _textPanel.Controls.Add(processedCard, 1, 0);

            // ============================
            // 3.B CLIPBOARD INSPECTOR PANEL (Phase 1)
            // ============================
            _inspectorPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Visible = false
            };

            var inspectorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            inspectorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240F));
            inspectorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // Cột bên trái: Danh sách định dạng
            var formatCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBgCard,
                Padding = new Padding(16, 12, 16, 16),
                Margin = new Padding(0, 0, 8, 0)
            };
            formatCard.Paint += (s, e) => DrawCardBorder(formatCard, e.Graphics, ColorAccentBlue);

            var formatTitle = new Label
            {
                Text = "CÁC ĐỊNH DẠNG CLIPBOARD",
                ForeColor = ColorAccentBlue,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 22
            };

            _formatListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBgCard,
                ForeColor = ColorTextPrimary,
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.None,
                Cursor = Cursors.Hand
            };
            _formatListBox.SelectedIndexChanged += FormatListBox_SelectedIndexChanged;

            formatCard.Controls.Add(_formatListBox);
            formatCard.Controls.Add(formatTitle);

            // Cột bên phải: Nội dung chi tiết
            var contentCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBgCard,
                Padding = new Padding(16, 12, 16, 16),
                Margin = new Padding(8, 0, 0, 0)
            };
            contentCard.Paint += (s, e) => DrawCardBorder(contentCard, e.Graphics, ColorAccentGreen);

            var contentTitle = new Label
            {
                Text = "NỘI DUNG CHI TIẾT CỦA FORMAT",
                ForeColor = ColorAccentGreen,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 22
            };

            _formatContentBox = new RichTextBox
            {
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                ReadOnly = true,
                BackColor = ColorBgCard,
                ForeColor = ColorTextPrimary,
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill
            };

            contentCard.Controls.Add(_formatContentBox);
            contentCard.Controls.Add(contentTitle);

            inspectorLayout.Controls.Add(formatCard, 0, 0);
            inspectorLayout.Controls.Add(contentCard, 1, 0);

            _inspectorPanel.Controls.Add(inspectorLayout);

            contentPanel.Controls.Add(_inspectorPanel);
            contentPanel.Controls.Add(_textPanel);

            // ============================
            // ASSEMBLY
            // ============================
            this.Controls.Add(contentPanel);
            this.Controls.Add(sidebarPanel);
            this.Controls.Add(titlePanel);

            // Cập nhật hiển thị Tab mặc định
            UpdateTabSelection();

            // Nạp dữ liệu mặc định
            RefreshHistoryList();

            EnableSvgDrop(this);
        }

        // ============================
        // KÉO-THẢ SVG → PNG
        // ============================

        /// <summary>
        /// Các control con phủ kín form nên sự kiện kéo-thả phải gắn lên từng control, không chỉ form.
        /// RichTextBox bị bỏ qua vì tự xử lý OLE drag-drop theo cách riêng.
        /// </summary>
        private void EnableSvgDrop(Control control)
        {
            if (control is not RichTextBox)
            {
                control.AllowDrop = true;
                control.DragEnter += SvgDrop_DragEnter;
                control.DragDrop += SvgDrop_DragDrop;
            }

            foreach (Control child in control.Controls)
            {
                EnableSvgDrop(child);
            }
        }

        private static bool TryGetDraggedSvgPaths(IDataObject? data, out string[] paths)
        {
            paths = Array.Empty<string>();
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
            {
                return false;
            }

            if (data.GetData(DataFormats.FileDrop) is not string[] dropped)
            {
                return false;
            }

            paths = dropped.Where(SvgClipboardSource.IsSvgPath).ToArray();
            return paths.Length > 0;
        }

        private void SvgDrop_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = TryGetDraggedSvgPaths(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void SvgDrop_DragDrop(object? sender, DragEventArgs e)
        {
            if (!TryGetDraggedSvgPaths(e.Data, out string[] paths))
            {
                return;
            }

            string path = paths[0];
            string fileName = System.IO.Path.GetFileName(path);

            string? markup = SvgClipboardSource.ReadSvgFile(path);
            if (markup == null)
            {
                _statusLabel.Text = $"⚠️ Không đọc được {fileName}";
                return;
            }

            _statusLabel.Text = $"⏳ Đang chuyển {fileName} sang PNG...";

            var payload = new SvgPayload
            {
                Markup = markup,
                SourceLabel = $"file {fileName}",
                SuggestedName = System.IO.Path.GetFileNameWithoutExtension(path)
            };

            SvgPasteResult result = await SvgPasteService.ConvertAndPublishAsync(payload, new[] { path });

            if (IsDisposed || Disposing)
            {
                return;
            }

            _statusLabel.Text = result.Success ? $"✅ {result.Message}" : $"⚠️ {result.Message}";
        }

        private void DrawCardBorder(Panel p, Graphics g, Color accentColor)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = GetRoundedPath(new Rectangle(0, 0, p.Width - 1, p.Height - 1), 8))
            {
                // Tô nền card
                using (var bgBrush = new SolidBrush(ColorBgCard))
                {
                    g.FillPath(bgBrush, path);
                }
                // Vẽ viền nhẹ
                using (var pen = new Pen(Color.FromArgb(55, 55, 60), 1))
                {
                    g.DrawPath(pen, path);
                }
                // Điểm nhấn accent dòng kẻ trên đầu card
                using (var brush = new SolidBrush(accentColor))
                {
                    g.FillRectangle(brush, 10, 0, 60, 3);
                }
            }
        }

        // ============================
        // RENDER SIDEBAR ITEMS (Tự vẽ nâng cấp thiết kế)
        // ============================

        private void HistoryList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _historyList.Items.Count) return;

            var item = _historyList.Items[e.Index] as ClipboardHistoryItem;
            if (item == null) return;

            bool isSelected = (e.State & DrawItemState.Selected) != 0;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Xóa nền
            using (var bgBrush = new SolidBrush(ColorBgSidebar))
            {
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
            }

            // Vẽ khối nổi cho item được chọn
            Rectangle itemRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 4, e.Bounds.Width - 12, e.Bounds.Height - 8);
            if (isSelected)
            {
                using (var path = GetRoundedPath(itemRect, 6))
                using (var selectBrush = new SolidBrush(Color.FromArgb(38, 38, 44)))
                {
                    e.Graphics.FillPath(selectBrush, path);
                }
            }

            // Màu chỉ thị trạng thái an toàn / xử lý
            Color indicatorColor = ColorTextMuted;
            if (item.PiiResult?.HasPii == true)
            {
                indicatorColor = item.Status.Contains("khôi phục") ? ColorAccentWarning : Color.FromArgb(231, 76, 60); // Đỏ nếu còn nhạy cảm hoặc cam nếu khôi phục
            }
            else if (item.Status == "Đã xử lý" || item.Status.Contains("Mặc định"))
            {
                indicatorColor = ColorAccentGreen;
            }

            // Vẽ vạch chỉ thị (bo góc tròn)
            using (var path = GetRoundedPath(new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 12, 4, e.Bounds.Height - 24), 2))
            using (var brush = new SolidBrush(indicatorColor))
            {
                e.Graphics.FillPath(brush, path);
            }

            // Vẽ thẻ Tag cho Tên Ứng dụng (App Badge)
            string app = item.AppName.ToUpper();
            if (app.Length > 12) app = app.Substring(0, 10) + "..";
            
            Rectangle appTagRect = new Rectangle(e.Bounds.X + 28, e.Bounds.Y + 10, 75, 18);
            using (var tagPath = GetRoundedPath(appTagRect, 4))
            using (var tagBg = new SolidBrush(Color.FromArgb(40, 40, 48)))
            {
                e.Graphics.FillPath(tagBg, tagPath);
                using (var font = new Font("Segoe UI", 7f, FontStyle.Bold))
                using (var brush = new SolidBrush(ColorTextMuted))
                {
                    var size = e.Graphics.MeasureString(app, font);
                    e.Graphics.DrawString(app, font, brush, 
                        appTagRect.X + (appTagRect.Width - size.Width) / 2, 
                        appTagRect.Y + (appTagRect.Height - size.Height) / 2);
                }
            }

            // Vẽ Thời gian (Timestamp)
            string time = item.Timestamp.ToString("HH:mm:ss");
            using (var font = new Font("Segoe UI Semibold", 8f))
            using (var brush = new SolidBrush(ColorTextMuted))
            {
                e.Graphics.DrawString(time, font, brush, e.Bounds.X + 112, e.Bounds.Y + 11);
            }

            // Vẽ Preview Text
            using (var font = new Font("Segoe UI", 8.5f))
            using (var brush = new SolidBrush(ColorTextPrimary))
            {
                e.Graphics.DrawString(item.PreviewText, font, brush,
                    new RectangleF(e.Bounds.X + 28, e.Bounds.Y + 34, e.Bounds.Width - 45, 20));
            }

            // Vẽ Badge nhãn PII nếu phát hiện dữ liệu nhạy cảm
            if (item.PiiResult?.HasPii == true)
            {
                string badgeText = item.Status.Contains("khôi phục") ? "REVERTED" : "DLP 🛡️";
                Color badgeBgColor = item.Status.Contains("khôi phục") ? ColorAccentWarning : Color.FromArgb(231, 76, 60);

                using (var font = new Font("Segoe UI", 7f, FontStyle.Bold))
                {
                    var textSize = e.Graphics.MeasureString(badgeText, font);
                    int badgeW = (int)textSize.Width + 10;
                    Rectangle badgeRect = new Rectangle(e.Bounds.Right - badgeW - 12, e.Bounds.Y + 10, badgeW, 18);
                    
                    using (var badgePath = GetRoundedPath(badgeRect, 9))
                    using (var badgeBrush = new SolidBrush(badgeBgColor))
                    {
                        e.Graphics.FillPath(badgeBrush, badgePath);
                        e.Graphics.DrawString(badgeText, font, Brushes.White, 
                            badgeRect.X + (badgeRect.Width - textSize.Width) / 2, 
                            badgeRect.Y + (badgeRect.Height - textSize.Height) / 2);
                    }
                }
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
            foreach (var item in _historySource.AsEnumerable().Reverse())
            {
                _historyList.Items.Add(item);
            }
            if (_historyList.Items.Count > 0)
            {
                _historyList.SelectedIndex = 0;
            }
            UpdateSecurityStatusIndicator();
        }

        private void UpdateSecurityStatusIndicator()
        {
            string saveToDisk = AppConfig.Get("SAVE_HISTORY_TO_DISK") ?? "true";
            string encryptHistory = AppConfig.Get("ENCRYPT_HISTORY_DATA") ?? "true";

            if (saveToDisk.ToLower() == "false")
            {
                _securityStatusLabel.Text = "⚡ CHỈ LƯU TRÊN BỘ NHỚ RAM";
                _securityStatusLabel.ForeColor = ColorAccentBlue;
            }
            else if (encryptHistory.ToLower() == "true")
            {
                _securityStatusLabel.Text = "🛡️ ĐÃ MÃ HÓA CỤC BỘ (DPAPI)";
                _securityStatusLabel.ForeColor = ColorAccentGreen;
            }
            else
            {
                _securityStatusLabel.Text = "🔓 LƯU TRỮ VĂN BẢN RÕ";
                _securityStatusLabel.ForeColor = ColorAccentWarning;
            }
        }

        private void RefreshDetailView()
        {
            if (_historyList.SelectedItem is not ClipboardHistoryItem item) return;

            // Status Label
            _statusLabel.Text = $"{item.Timestamp:yyyy-MM-dd HH:mm:ss}  |  Nguồn: {item.AppName.ToUpper()}  |  Trạng thái: {item.Status}";

            // PII Warning Banner
            if (item.PiiResult?.HasPii == true)
            {
                if (item.Status.Contains("khôi phục"))
                {
                    _piiWarningLabel.Text = $"⚠️ ĐÃ KHÔI PHỤC BẢN GỐC NHẠY CẢM: {item.PiiResult.GetSummary()} ({item.PiiResult.Matches.Count} mục)";
                    _piiWarningLabel.BackColor = ColorAccentWarning;
                }
                else
                {
                    _piiWarningLabel.Text = $"🛡️ ĐÃ TỰ ĐỘNG MÃ HÓA BẢO VỆ: {item.PiiResult.GetSummary()} ({item.PiiResult.Matches.Count} mục)";
                    _piiWarningLabel.BackColor = ColorAccentGreen;
                }
                _piiWarningLabel.Height = 32;
            }
            else
            {
                _piiWarningLabel.Text = "";
                _piiWarningLabel.Height = 0;
            }

            // Nội dung hiển thị
            if (_showUnmasked || item.PiiResult == null || !item.PiiResult.HasPii)
            {
                _originalBox.Text = item.OriginalText;
            }
            else
            {
                _originalBox.Text = item.PiiResult.GetSanitizedText(item.OriginalText);
            }

            _processedBox.Text = !string.IsNullOrEmpty(item.ProcessedText) ? item.ProcessedText : "(Không thay đổi)";

            // Cập nhật danh sách các định dạng Clipboard (Phase 1)
            _formatListBox.Items.Clear();
            if (item.ClipboardFormats != null && item.ClipboardFormats.Count > 0)
            {
                foreach (var format in item.ClipboardFormats.Keys)
                {
                    _formatListBox.Items.Add(format);
                }
                _formatListBox.SelectedIndex = 0;
            }
            else
            {
                _formatListBox.Items.Add("(Không có dữ liệu)");
                _formatContentBox.Text = "(Không có dữ liệu định dạng)";
            }
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

        // Helper vẽ bo góc GDI+
        private static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            
            // Đề phòng kích thước nhỏ hơn đường kính
            if (rect.Width <= diameter) diameter = rect.Width - 1;
            if (rect.Height <= diameter) diameter = rect.Height - 1;
            if (diameter <= 0) diameter = 1;

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Clipboard Inspector Helper Methods (Phase 1)
        private void FormatListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_historyList.SelectedItem is not ClipboardHistoryItem item) return;

            string? selectedFormat = _formatListBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedFormat) && item.ClipboardFormats.TryGetValue(selectedFormat, out string? content))
            {
                _formatContentBox.Text = content;
            }
            else
            {
                _formatContentBox.Text = "(Không có nội dung)";
            }
        }

        private void UpdateTabSelection()
        {
            if (_isInInspectorMode)
            {
                _btnPreview.ForeColor = ColorTextMuted;
                _btnPreview.FlatAppearance.BorderColor = Color.FromArgb(55, 55, 60);
                _btnPreview.BackColor = Color.Transparent;

                _btnInspector.ForeColor = Color.White;
                _btnInspector.FlatAppearance.BorderColor = ColorAccentGreen;
                _btnInspector.BackColor = Color.FromArgb(38, 38, 44);

                _textPanel.Visible = false;
                _inspectorPanel.Visible = true;
            }
            else
            {
                _btnPreview.ForeColor = Color.White;
                _btnPreview.FlatAppearance.BorderColor = ColorAccentGreen;
                _btnPreview.BackColor = Color.FromArgb(38, 38, 44);

                _btnInspector.ForeColor = ColorTextMuted;
                _btnInspector.FlatAppearance.BorderColor = Color.FromArgb(55, 55, 60);
                _btnInspector.BackColor = Color.Transparent;

                _textPanel.Visible = true;
                _inspectorPanel.Visible = false;
            }
        }
    }
}
