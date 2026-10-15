using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Một mục trong lịch sử clipboard (có cache preview siêu tốc, tránh xử lý chuỗi lớn khi lăn chuột).
    /// </summary>
    public class ClipboardHistoryItem
    {
        private string? _cachedPreview;
        private string _originalText = "";
        private string _processedText = "";

        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string AppName { get; set; } = "Unknown";
        public string OriginalText
        {
            get => _originalText;
            set { _originalText = value; _cachedPreview = null; }
        }
        public string OriginalHtml { get; set; } = "";
        public string ProcessedText
        {
            get => _processedText;
            set { _processedText = value; _cachedPreview = null; }
        }
        public string ProcessedHtml { get; set; } = "";
        public string Status { get; set; } = "Bỏ qua";
        public PiiScanResult? PiiResult { get; set; }
        public Dictionary<string, string> ClipboardFormats { get; set; } = new Dictionary<string, string>();

        public string PreviewText
        {
            get
            {
                if (_cachedPreview != null) return _cachedPreview;
                string src = !string.IsNullOrEmpty(_processedText) ? _processedText : _originalText;
                if (string.IsNullOrWhiteSpace(src)) return _cachedPreview = "(Không có nội dung văn bản)";
                // Tối ưu hóa tuyệt đối: Chỉ lấy 120 ký tự đầu tiên để format, tránh xử lý chuỗi 100KB gây lag khi lăn chuột
                if (src.Length > 120) src = src.Substring(0, 120);
                src = src.Replace("\r", " ").Replace("\n", " ").Trim();
                _cachedPreview = src.Length > 65 ? src.Substring(0, 65) + "…" : src;
                return _cachedPreview;
            }
        }
    }

    /// <summary>
    /// ListBox có hỗ trợ Double-Buffering phần cứng qua WS_EX_COMPOSITED, loại bỏ hoàn toàn hiện tượng giật/lag khi lăn chuột.
    /// </summary>
    internal sealed class SmoothListBox : ListBox
    {
        public SmoothListBox()
        {
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw,
                true);
            DoubleBuffered = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED: Bật offscreen buffer mượt mà
                return cp;
            }
        }
    }

    /// <summary>
    /// Cửa sổ lịch sử clipboard dạng Mini Popup tinh gọn, responsive, mượt mà 60 FPS.
    /// </summary>
    public class ClipboardHistoryForm : Form
    {
        // Cache tĩnh các tài nguyên GDI để không tạo rác/GC khi lăn chuột liên tục
        private static readonly SolidBrush _surfaceBrush = new(UiTheme.Surface);
        private static readonly SolidBrush _selectedBgBrush = new(UiTheme.AccentLight);
        private static readonly SolidBrush _selectedBarBrush = new(UiTheme.Accent);
        private static readonly SolidBrush _metaBrush = new(UiTheme.TextMuted);
        private static readonly SolidBrush _textPrimaryBrush = new(UiTheme.TextPrimary);
        private static readonly SolidBrush _accentHoverBrush = new(UiTheme.AccentHover);
        private static readonly SolidBrush _piiBgBrush = new(Color.FromArgb(220, 252, 231));
        private static readonly SolidBrush _piiTextBrush = new(Color.FromArgb(22, 101, 52));
        private static readonly Pen _borderPen = new(UiTheme.Border, 1);
        private static readonly Font _badgeFont = new("Segoe UI", 7.5f, FontStyle.Bold);
        private static readonly StringFormat _previewFormat = new()
        {
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.NoWrap
        };

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED: Loại bỏ hoàn toàn giật/chớp tắt toàn form
                return cp;
            }
        }

        private readonly List<ClipboardHistoryItem> _historySource;
        private readonly List<ClipboardHistoryItem> _filteredList = new();
        private readonly SmoothListBox _historyList;
        private readonly TextBox _searchBox;
        private readonly Label _countLabel;
        private readonly Panel _emptyPanel;
        private readonly Button _copyButton;
        private readonly Button _deleteButton;
        private readonly Button _clearAllButton;
        private readonly Button _closeButton;

        public ClipboardHistoryForm(List<ClipboardHistoryItem> history)
        {
            _historySource = history;

            UiTheme.ApplyDialog(this, "HapiCopi — Lịch sử Clipboard");
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // Kích thước chuẩn Mini Popup gọn gàng, tương đồng với Math Editor thu nhỏ
            MinimumSize = new Size(LogicalToDeviceUnits(440), LogicalToDeviceUnits(380));
            ClientSize = new Size(LogicalToDeviceUnits(480), LogicalToDeviceUnits(480));
            KeyPreview = true;
            FormClosed += (s, e) => MemoryOptimizer.OptimizeMemory();

            // 1. Header Bar: Tiêu đề + Đếm số mục + Nút Xóa sạch
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(LogicalToDeviceUnits(14), LogicalToDeviceUnits(12),
                                      LogicalToDeviceUnits(14), LogicalToDeviceUnits(8)),
                BackColor = UiTheme.Surface
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                Text = "Lịch sử Clipboard",
                Font = UiTheme.SectionTitle,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0),
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };

            _countLabel = new Label
            {
                Text = "",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, LogicalToDeviceUnits(2), 0, 0)
            };

            _clearAllButton = new Button
            {
                Text = "Xóa tất cả",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Danger,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Margin = Padding.Empty
            };
            _clearAllButton.FlatAppearance.BorderSize = 0;
            _clearAllButton.Click += (s, e) => ClearAllHistory();

            header.Controls.Add(titleLabel, 0, 0);
            header.Controls.Add(_countLabel, 1, 0);
            header.Controls.Add(_clearAllButton, 2, 0);

            // 2. Ô tìm kiếm nhanh (Instant Filter)
            var searchPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = LogicalToDeviceUnits(36),
                Padding = new Padding(LogicalToDeviceUnits(14), 0, LogicalToDeviceUnits(14), LogicalToDeviceUnits(6)),
                BackColor = UiTheme.Surface
            };

            _searchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.Body,
                PlaceholderText = "Tìm kiếm nội dung đã sao chép...",
                BorderStyle = BorderStyle.FixedSingle
            };
            _searchBox.TextChanged += (s, e) => FilterHistory();
            searchPanel.Controls.Add(_searchBox);

            var headerBorder = new Panel
            {
                Dock = DockStyle.Top,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            // 3. Body: Danh sách lịch sử gọn gàng
            var listHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(6), LogicalToDeviceUnits(4), LogicalToDeviceUnits(6), 0)
            };

            _historyList = new SmoothListBox
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.Body,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = LogicalToDeviceUnits(54),
                IntegralHeight = false
            };
            _historyList.DrawItem += HistoryList_DrawItem;
            _historyList.DoubleClick += (s, e) => CopySelectedItem();
            _historyList.SelectedIndexChanged += (s, e) => UpdateButtonStates();

            // Màn hình trống
            _emptyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                Visible = false
            };
            var emptyLabel = new Label
            {
                Text = "Chưa có nội dung sao chép nào.\n\nSao chép văn bản ở bất kỳ đâu — HapiCopi sẽ lưu lại tại đây.",
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            _emptyPanel.Controls.Add(emptyLabel);

            listHost.Controls.Add(_historyList);
            listHost.Controls.Add(_emptyPanel);

            // 4. Footer Bar: Nút hành động nhanh (Copy, Xóa mục, Đóng)
            var footerBorder = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = LogicalToDeviceUnits(58),
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(12),
                                      LogicalToDeviceUnits(16), LogicalToDeviceUnits(12))
            };

            var footerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = Padding.Empty
            };
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _deleteButton = new Button
            {
                Text = "Xóa mục",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0)
            };
            _deleteButton.FlatAppearance.BorderSize = 0;
            _deleteButton.Click += (s, e) => DeleteSelectedItem();

            var rightButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = Padding.Empty
            };

            _copyButton = UiTheme.PrimaryButton("Sao chép (Enter)");
            _copyButton.Click += (s, e) => CopySelectedItem();

            _closeButton = UiTheme.SecondaryButton("Đóng (Esc)");
            _closeButton.Click += (s, e) => Close();
            _closeButton.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0);

            rightButtons.Controls.Add(_copyButton);
            rightButtons.Controls.Add(_closeButton);

            footerLayout.Controls.Add(_deleteButton, 0, 0);
            footerLayout.Controls.Add(rightButtons, 1, 0);
            footer.Controls.Add(footerLayout);

            // Đưa tất cả vào Form
            Controls.Add(listHost);
            Controls.Add(headerBorder);
            Controls.Add(searchPanel);
            Controls.Add(header);
            Controls.Add(footerBorder);
            Controls.Add(footer);
            listHost.BringToFront();

            KeyDown += ClipboardHistoryForm_KeyDown;
            RefreshHistoryList();
        }

        private void ClipboardHistoryForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && !e.Control && !e.Alt)
            {
                CopySelectedItem();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete && !_searchBox.Focused)
            {
                DeleteSelectedItem();
                e.Handled = true;
            }
        }

        public void RefreshHistoryList()
        {
            FilterHistory();
        }

        private void FilterHistory()
        {
            string query = _searchBox.Text.Trim().ToLowerInvariant();
            _filteredList.Clear();

            var items = _historySource.AsEnumerable().Reverse();
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(query) ||
                    (item.OriginalText?.ToLowerInvariant().Contains(query) == true) ||
                    (item.ProcessedText?.ToLowerInvariant().Contains(query) == true) ||
                    (item.AppName?.ToLowerInvariant().Contains(query) == true))
                {
                    _filteredList.Add(item);
                }
            }

            _historyList.BeginUpdate();
            _historyList.Items.Clear();
            foreach (var item in _filteredList)
            {
                _historyList.Items.Add(item);
            }
            _historyList.EndUpdate();

            bool empty = _historyList.Items.Count == 0;
            _emptyPanel.Visible = empty;
            _countLabel.Text = $"({_filteredList.Count} mục)";

            if (!empty && _historyList.SelectedIndex < 0)
            {
                _historyList.SelectedIndex = 0;
            }

            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            bool hasSelection = _historyList.SelectedItem is ClipboardHistoryItem;
            _copyButton.Enabled = hasSelection;
            _deleteButton.Enabled = hasSelection;
            _clearAllButton.Enabled = _historySource.Count > 0;
        }

        private void CopySelectedItem()
        {
            if (_historyList.SelectedItem is not ClipboardHistoryItem item) return;

            string textToCopy = !string.IsNullOrEmpty(item.ProcessedText) ? item.ProcessedText : item.OriginalText;
            if (string.IsNullOrEmpty(textToCopy)) return;

            try
            {
                Clipboard.SetText(textToCopy);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không thể sao chép vào clipboard: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeleteSelectedItem()
        {
            if (_historyList.SelectedItem is not ClipboardHistoryItem item) return;

            _historySource.Remove(item);
            HistoryManager.SaveHistory(_historySource);
            FilterHistory();
        }

        private void ClearAllHistory()
        {
            if (_historySource.Count == 0) return;

            if (MessageBox.Show(this, "Bạn có chắc chắn muốn xóa toàn bộ lịch sử clipboard?", "Xác nhận",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _historySource.Clear();
                HistoryManager.SaveHistory(_historySource);
                FilterHistory();
            }
        }

        private void HistoryList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _historyList.Items.Count) return;
            if (_historyList.Items[e.Index] is not ClipboardHistoryItem item) return;

            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None; // Tối ưu vẽ hình học siêu tốc 60 FPS

            // Nền thẻ
            g.FillRectangle(isSelected ? _selectedBgBrush : _surfaceBrush, e.Bounds);

            if (isSelected)
            {
                g.FillRectangle(_selectedBarBrush, e.Bounds.Left, e.Bounds.Top, LogicalToDeviceUnits(3), e.Bounds.Height);
            }

            int left = e.Bounds.Left + LogicalToDeviceUnits(10);
            int top = e.Bounds.Top + LogicalToDeviceUnits(6);
            int right = e.Bounds.Right - LogicalToDeviceUnits(10);

            // Dòng 1: AppName + Timestamp + PII Badge
            string timeStr = item.Timestamp.ToString("HH:mm:ss");
            string metaText = $"{item.AppName}  ·  {timeStr}";
            g.DrawString(metaText, UiTheme.Small, _metaBrush, left, top);

            if (item.PiiResult?.HasPii == true)
            {
                int bWidth = LogicalToDeviceUnits(62);
                int bHeight = LogicalToDeviceUnits(16);
                int bLeft = right - bWidth;
                int bTop = top;
                var bRect = new Rectangle(bLeft, bTop, bWidth, bHeight);

                g.FillRectangle(_piiBgBrush, bRect);
                g.DrawString("Đã che PII", _badgeFont, _piiTextBrush, bLeft + LogicalToDeviceUnits(4), bTop + LogicalToDeviceUnits(1));
            }

            // Dòng 2: Nội dung trích đoạn (PreviewText)
            top += LogicalToDeviceUnits(18);
            string preview = item.PreviewText;
            var textRect = new RectangleF(left, top, right - left, e.Bounds.Bottom - top - LogicalToDeviceUnits(4));
            g.DrawString(preview, UiTheme.Body, isSelected ? _accentHoverBrush : _textPrimaryBrush, textRect, _previewFormat);

            // Đường viền ngăn cách
            g.DrawLine(_borderPen, e.Bounds.Left + LogicalToDeviceUnits(6), e.Bounds.Bottom - 1, e.Bounds.Right - LogicalToDeviceUnits(6), e.Bounds.Bottom - 1);
        }
    }
}
