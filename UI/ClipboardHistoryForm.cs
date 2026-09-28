using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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
        public string Status { get; set; } = "Bỏ qua";
        public PiiScanResult? PiiResult { get; set; }
        public Dictionary<string, string> ClipboardFormats { get; set; } = new Dictionary<string, string>();

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
    /// Cửa sổ lịch sử clipboard: xem bản gốc / bản đã xử lý, thanh tra format, kéo thả SVG.
    /// </summary>
    public class ClipboardHistoryForm : Form
    {
        private readonly List<ClipboardHistoryItem> _historySource;
        private readonly ListBox _historyList;
        private readonly RichTextBox _originalBox;
        private readonly RichTextBox _processedBox;
        private readonly Label _statusLabel;
        private readonly Label _piiWarningLabel;
        private readonly Label _securityStatusLabel;
        private readonly Label _emptyLabel;
        private readonly Panel _emptyPanel;
        private readonly CheckBox _showRawCheckbox;
        private bool _showUnmasked;

        private readonly TableLayoutPanel _textPanel;
        private readonly Panel _inspectorPanel;
        private readonly ListBox _formatListBox;
        private readonly RichTextBox _formatContentBox;
        private readonly Button _btnPreview;
        private readonly Button _btnInspector;
        private bool _isInInspectorMode;

        public ClipboardHistoryForm(List<ClipboardHistoryItem> history)
        {
            _historySource = history;

            UiTheme.ApplyDialog(this, "HapiCopi — Lịch sử Clipboard");
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            MinimumSize = new Size(LogicalToDeviceUnits(800), LogicalToDeviceUnits(500));
            Size = new Size(LogicalToDeviceUnits(1000), LogicalToDeviceUnits(640));
            FormClosed += (s, e) => MemoryOptimizer.OptimizeMemory();

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(10),
                                      LogicalToDeviceUnits(16), LogicalToDeviceUnits(10)),
                BackColor = UiTheme.Surface
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var title = UiTheme.PageTitleLabel("Lịch sử Clipboard");
            title.Dock = DockStyle.Fill;
            header.Controls.Add(title, 0, 0);

            _securityStatusLabel = new Label
            {
                AutoSize = true,
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleRight,
                UseMnemonic = false,
                Anchor = AnchorStyles.Right
            };
            header.Controls.Add(_securityStatusLabel, 1, 0);

            var headerBorder = new Panel
            {
                Dock = DockStyle.Top,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            var sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = LogicalToDeviceUnits(280),
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(12))
            };

            var sidebarTitle = new Label
            {
                Text = "Sự kiện gần đây",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.Accent,
                Dock = DockStyle.Top,
                AutoSize = true,
                UseMnemonic = false,
                Padding = new Padding(0, 0, 0, LogicalToDeviceUnits(8))
            };

            Button clearButton = UiTheme.SecondaryButton("Xóa lịch sử");
            clearButton.Dock = DockStyle.Bottom;
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

            _historyList = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.Body,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = LogicalToDeviceUnits(58),
                IntegralHeight = false
            };
            _historyList.DrawItem += HistoryList_DrawItem;
            _historyList.SelectedIndexChanged += HistoryList_SelectedIndexChanged;

            sidebar.Controls.Add(_historyList);
            sidebar.Controls.Add(sidebarTitle);
            sidebar.Controls.Add(clearButton);

            var sidebarBorder = new Panel
            {
                Dock = DockStyle.Left,
                Width = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            var content = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(LogicalToDeviceUnits(16)),
                BackColor = UiTheme.Background
            };

            var statusRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(8))
            };
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _statusLabel = new Label
            {
                Text = "Chọn một mục bên trái để xem chi tiết",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                UseMnemonic = false,
                Tag = UiTheme.WrapTag,
                Dock = DockStyle.Fill
            };
            statusRow.Controls.Add(_statusLabel, 0, 0);

            var tabButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            _btnPreview = UiTheme.PrimaryButton("Xem trước");
            _btnPreview.Click += (s, e) => { _isInInspectorMode = false; UpdateTabSelection(); };
            _btnInspector = UiTheme.SecondaryButton("Thanh tra");
            _btnInspector.Click += (s, e) => { _isInInspectorMode = true; UpdateTabSelection(); };
            _btnPreview.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0);
            tabButtons.Controls.Add(_btnPreview);
            tabButtons.Controls.Add(_btnInspector);
            statusRow.Controls.Add(tabButtons, 1, 0);

            _piiWarningLabel = new Label
            {
                Text = "",
                ForeColor = Color.White,
                BackColor = UiTheme.Danger,
                Font = UiTheme.BodyStrong,
                Dock = DockStyle.Top,
                Height = 0,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(LogicalToDeviceUnits(12), 0, LogicalToDeviceUnits(12), 0),
                UseMnemonic = false
            };

            var actionBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(0, LogicalToDeviceUnits(10), 0, 0),
                BackColor = Color.Transparent
            };

            Button copyOriginalBtn = UiTheme.SecondaryButton("Sao chép bản gốc");
            copyOriginalBtn.Click += (s, e) => CopySelectedToClipboard(original: true);
            copyOriginalBtn.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), LogicalToDeviceUnits(4));

            Button copyProcessedBtn = UiTheme.PrimaryButton("Sao chép bản mới");
            copyProcessedBtn.Click += (s, e) => CopySelectedToClipboard(original: false);
            copyProcessedBtn.Margin = new Padding(0, 0, LogicalToDeviceUnits(16), LogicalToDeviceUnits(4));

            _showRawCheckbox = UiTheme.Option("Hiện thông tin gốc (bỏ che)");
            _showRawCheckbox.Font = UiTheme.Small;
            _showRawCheckbox.CheckedChanged += (s, e) =>
            {
                _showUnmasked = _showRawCheckbox.Checked;
                RefreshDetailView();
            };

            actionBar.Controls.Add(copyOriginalBtn);
            actionBar.Controls.Add(copyProcessedBtn);
            actionBar.Controls.Add(_showRawCheckbox);

            _textPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            _textPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _textPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _textPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _textPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var originalTitle = new Label
            {
                Text = "Nội dung gốc (khi copy)",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.Warning,
                AutoSize = true,
                UseMnemonic = false,
                Margin = new Padding(0, 0, LogicalToDeviceUnits(8), LogicalToDeviceUnits(4))
            };
            var processedTitle = new Label
            {
                Text = "Nội dung sau xử lý (bản mới)",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.Success,
                AutoSize = true,
                UseMnemonic = false,
                Margin = new Padding(LogicalToDeviceUnits(8), 0, 0, LogicalToDeviceUnits(4))
            };

            _originalBox = MakeReadBox();
            _originalBox.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0);
            _processedBox = MakeReadBox();
            _processedBox.Margin = new Padding(LogicalToDeviceUnits(8), 0, 0, 0);

            _textPanel.Controls.Add(originalTitle, 0, 0);
            _textPanel.Controls.Add(processedTitle, 1, 0);
            _textPanel.Controls.Add(_originalBox, 0, 1);
            _textPanel.Controls.Add(_processedBox, 1, 1);

            _inspectorPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
            var inspectorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            inspectorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            inspectorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            inspectorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var formatTitle = new Label
            {
                Text = "Các định dạng clipboard",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.Accent,
                AutoSize = true,
                UseMnemonic = false,
                Margin = new Padding(0, 0, LogicalToDeviceUnits(8), LogicalToDeviceUnits(4))
            };
            var contentTitle = new Label
            {
                Text = "Nội dung của định dạng",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.Success,
                AutoSize = true,
                UseMnemonic = false,
                Margin = new Padding(LogicalToDeviceUnits(8), 0, 0, LogicalToDeviceUnits(4))
            };

            _formatListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0)
            };
            _formatListBox.SelectedIndexChanged += FormatListBox_SelectedIndexChanged;

            _formatContentBox = MakeReadBox();
            _formatContentBox.Margin = new Padding(LogicalToDeviceUnits(8), 0, 0, 0);

            inspectorLayout.Controls.Add(formatTitle, 0, 0);
            inspectorLayout.Controls.Add(contentTitle, 1, 0);
            inspectorLayout.Controls.Add(_formatListBox, 0, 1);
            inspectorLayout.Controls.Add(_formatContentBox, 1, 1);
            _inspectorPanel.Controls.Add(inspectorLayout);

            _emptyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Background
            };
            _emptyLabel = UiTheme.Description(
                "Chưa có mục nào.\n\nCopy văn bản ở bất kỳ đâu — HapiCopi sẽ ghi lại ở đây.\n" +
                "Kéo thả file .svg vào cửa sổ này để chuyển thành PNG ngay trên máy.");
            _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            _emptyLabel.Dock = DockStyle.Fill;
            _emptyPanel.Controls.Add(_emptyLabel);

            content.Controls.Add(_textPanel);
            content.Controls.Add(_inspectorPanel);
            content.Controls.Add(_emptyPanel);
            content.Controls.Add(_piiWarningLabel);
            content.Controls.Add(statusRow);
            content.Controls.Add(actionBar);

            Controls.Add(content);
            Controls.Add(sidebarBorder);
            Controls.Add(sidebar);
            Controls.Add(headerBorder);
            Controls.Add(header);

            content.ClientSizeChanged += (s, e) =>
                UiTheme.ApplyWrapWidth(content, content.ClientSize.Width - content.Padding.Horizontal);

            UpdateTabSelection();
            RefreshHistoryList();
            EnableSvgDrop(this);
        }

        private RichTextBox MakeReadBox()
        {
            return new RichTextBox
            {
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                ReadOnly = true,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill
            };
        }

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
                _statusLabel.Text = $"Không đọc được {fileName}";
                return;
            }

            _statusLabel.Text = $"Đang chuyển {fileName} sang PNG...";

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

            _statusLabel.Text = result.Message;
        }

        private void HistoryList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _historyList.Items.Count)
            {
                return;
            }

            if (_historyList.Items[e.Index] is not ClipboardHistoryItem item)
            {
                return;
            }

            bool isSelected = (e.State & DrawItemState.Selected) != 0;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bg = new SolidBrush(UiTheme.Surface))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            Rectangle itemRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 3, e.Bounds.Width - 8, e.Bounds.Height - 6);
            if (isSelected)
            {
                using var path = GetRoundedPath(itemRect, 6);
                using var selectBrush = new SolidBrush(Color.FromArgb(226, 236, 247));
                e.Graphics.FillPath(selectBrush, path);
            }

            Color indicatorColor = UiTheme.TextDisabled;
            if (item.PiiResult?.HasPii == true)
            {
                indicatorColor = item.Status.Contains("khôi phục") ? UiTheme.Warning : UiTheme.Danger;
            }
            else if (item.Status == "Đã xử lý" || item.Status.Contains("Mặc định"))
            {
                indicatorColor = UiTheme.Success;
            }

            using (var path = GetRoundedPath(new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 12, 4, e.Bounds.Height - 24), 2))
            using (var brush = new SolidBrush(indicatorColor))
            {
                e.Graphics.FillPath(brush, path);
            }

            string app = item.AppName.ToUpperInvariant();
            if (app.Length > 12)
            {
                app = app.Substring(0, 10) + "..";
            }

            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var brush = new SolidBrush(UiTheme.TextMuted))
            {
                e.Graphics.DrawString(app, font, brush, e.Bounds.X + 22, e.Bounds.Y + 8);
            }

            using (var font = new Font("Segoe UI Semibold", 8f))
            using (var brush = new SolidBrush(UiTheme.TextMuted))
            {
                e.Graphics.DrawString(item.Timestamp.ToString("HH:mm:ss"), font, brush, e.Bounds.X + 110, e.Bounds.Y + 8);
            }

            using (var font = new Font("Segoe UI", 8.5f))
            using (var brush = new SolidBrush(UiTheme.TextPrimary))
            {
                e.Graphics.DrawString(item.PreviewText, font, brush,
                    new RectangleF(e.Bounds.X + 22, e.Bounds.Y + 30, e.Bounds.Width - 36, 22));
            }

            if (item.PiiResult?.HasPii == true)
            {
                string badgeText = item.Status.Contains("khôi phục") ? "REVERTED" : "DLP";
                Color badgeBg = item.Status.Contains("khôi phục") ? UiTheme.Warning : UiTheme.Danger;
                using var font = new Font("Segoe UI", 7f, FontStyle.Bold);
                var textSize = e.Graphics.MeasureString(badgeText, font);
                int badgeW = (int)textSize.Width + 10;
                var badgeRect = new Rectangle(e.Bounds.Right - badgeW - 10, e.Bounds.Y + 8, badgeW, 16);
                using var badgePath = GetRoundedPath(badgeRect, 8);
                using var badgeBrush = new SolidBrush(badgeBg);
                e.Graphics.FillPath(badgeBrush, badgePath);
                e.Graphics.DrawString(badgeText, font, Brushes.White,
                    badgeRect.X + (badgeRect.Width - textSize.Width) / 2,
                    badgeRect.Y + (badgeRect.Height - textSize.Height) / 2);
            }
        }

        private void HistoryList_SelectedIndexChanged(object? sender, EventArgs e)
        {
            RefreshDetailView();
        }

        public void RefreshHistoryList()
        {
            _historyList.Items.Clear();
            foreach (var item in _historySource.AsEnumerable().Reverse())
            {
                _historyList.Items.Add(item);
            }

            bool empty = _historyList.Items.Count == 0;
            _emptyPanel.Visible = empty;
            _textPanel.Visible = !empty && !_isInInspectorMode;
            _inspectorPanel.Visible = !empty && _isInInspectorMode;

            if (!empty)
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
                _securityStatusLabel.Text = "Chỉ lưu trên RAM";
                _securityStatusLabel.ForeColor = UiTheme.Accent;
            }
            else if (encryptHistory.ToLower() == "true")
            {
                _securityStatusLabel.Text = "Đã mã hóa cục bộ (DPAPI)";
                _securityStatusLabel.ForeColor = UiTheme.Success;
            }
            else
            {
                _securityStatusLabel.Text = "Lưu văn bản rõ";
                _securityStatusLabel.ForeColor = UiTheme.Warning;
            }
        }

        private void RefreshDetailView()
        {
            if (_historyList.SelectedItem is not ClipboardHistoryItem item)
            {
                return;
            }

            _statusLabel.Text = $"{item.Timestamp:yyyy-MM-dd HH:mm:ss}  ·  Nguồn: {item.AppName}  ·  {item.Status}";

            if (item.PiiResult?.HasPii == true)
            {
                if (item.Status.Contains("khôi phục"))
                {
                    _piiWarningLabel.Text = $"Đã khôi phục bản gốc nhạy cảm: {item.PiiResult.GetSummary()} ({item.PiiResult.Matches.Count} mục)";
                    _piiWarningLabel.BackColor = UiTheme.Warning;
                }
                else
                {
                    _piiWarningLabel.Text = $"Đã tự động che: {item.PiiResult.GetSummary()} ({item.PiiResult.Matches.Count} mục)";
                    _piiWarningLabel.BackColor = UiTheme.Success;
                }
                _piiWarningLabel.Height = LogicalToDeviceUnits(32);
            }
            else
            {
                _piiWarningLabel.Text = "";
                _piiWarningLabel.Height = 0;
            }

            if (_showUnmasked || item.PiiResult == null || !item.PiiResult.HasPii)
            {
                _originalBox.Text = item.OriginalText;
            }
            else
            {
                _originalBox.Text = item.PiiResult.GetSanitizedText(item.OriginalText);
            }

            _processedBox.Text = !string.IsNullOrEmpty(item.ProcessedText) ? item.ProcessedText : "(Không thay đổi)";

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
            if (_historyList.SelectedItem is not ClipboardHistoryItem item)
            {
                return;
            }

            try
            {
                string textToCopy = original ? item.OriginalText : item.ProcessedText;
                if (string.IsNullOrEmpty(textToCopy))
                {
                    textToCopy = item.OriginalText;
                }

                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy);
                    _statusLabel.Text = original
                        ? "Đã sao chép bản gốc vào clipboard"
                        : "Đã sao chép bản đã xử lý vào clipboard";
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[History] Lỗi copy: {ex.Message}");
            }
        }

        private static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            if (rect.Width <= diameter) diameter = Math.Max(1, rect.Width - 1);
            if (rect.Height <= diameter) diameter = Math.Max(1, rect.Height - 1);

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void FormatListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_historyList.SelectedItem is not ClipboardHistoryItem item)
            {
                return;
            }

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
            bool empty = _historyList.Items.Count == 0;
            _emptyPanel.Visible = empty;

            if (_isInInspectorMode)
            {
                StyleTab(_btnPreview, selected: false);
                StyleTab(_btnInspector, selected: true);
                _textPanel.Visible = false;
                _inspectorPanel.Visible = !empty;
            }
            else
            {
                StyleTab(_btnPreview, selected: true);
                StyleTab(_btnInspector, selected: false);
                _textPanel.Visible = !empty;
                _inspectorPanel.Visible = false;
            }
        }

        private static void StyleTab(Button button, bool selected)
        {
            if (selected)
            {
                button.BackColor = UiTheme.Accent;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderSize = 0;
            }
            else
            {
                button.BackColor = UiTheme.Background;
                button.ForeColor = UiTheme.TextPrimary;
                button.FlatAppearance.BorderColor = UiTheme.Border;
                button.FlatAppearance.BorderSize = 1;
            }
        }
    }
}
