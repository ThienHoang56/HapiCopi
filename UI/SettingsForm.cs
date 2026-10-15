using System;
using System.Drawing;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Cửa sổ Cài đặt HapiCopi: Tinh gọn, responsive, tự giải thích (self-explanatory).
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly CheckBox _piiScanCheck;
        private readonly CheckBox _piiToastCheck;
        private readonly CheckBox _piiMaskHistoryCheck;
        private readonly CheckBox _svgToPngCheck;
        private readonly CheckBox _svgTransparentCheck;
        private readonly TextBox _customKeywordsBox;
        private readonly CheckBox _enableHistoryCheck;
        private readonly CheckBox _saveToDiskCheck;
        private readonly CheckBox _startWithWindowsCheck;
        private readonly System.Collections.Generic.List<ClipboardHistoryItem> _historyList;

        public SettingsForm(System.Collections.Generic.List<ClipboardHistoryItem> history)
        {
            _historyList = history;

            UiTheme.ApplyDialog(this, "HapiCopi — Cài đặt");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // Kích thước chuẩn Mini Popup vừa vặn, không bị to bự chảng
            MinimumSize = new Size(LogicalToDeviceUnits(440), LogicalToDeviceUnits(420));
            Size = new Size(LogicalToDeviceUnits(480), LogicalToDeviceUnits(480));
            KeyPreview = true;
            FormClosed += (s, e) => MemoryOptimizer.OptimizeMemory();

            // Header Bar
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = LogicalToDeviceUnits(44),
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(10), LogicalToDeviceUnits(16), 0),
                BackColor = UiTheme.Surface
            };
            var title = UiTheme.PageTitleLabel("Cài đặt");
            title.Dock = DockStyle.Fill;
            header.Controls.Add(title);

            var headerBorder = new Panel
            {
                Dock = DockStyle.Top,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            // Body Panel: Bố cục cột tự co giãn, không cuộn
            var content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Background,
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(10),
                                      LogicalToDeviceUnits(16), LogicalToDeviceUnits(8)),
                AutoScroll = true
            };

            var col = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            col.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            int row = 0;
            void AddControl(Control ctrl, int bottomGap = 6)
            {
                ctrl.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(bottomGap));
                col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                col.Controls.Add(ctrl, 0, row++);
            }

            // ============================
            // 1. BẢO MẬT CLIPBOARD (DLP)
            // ============================
            AddControl(UiTheme.SectionHeader("Bảo mật dữ liệu (DLP)", UiTheme.Accent), 6);
            _piiScanCheck = UiTheme.Option("Quét tự động thông tin nhạy cảm (PII)");
            AddControl(_piiScanCheck, 4);

            _piiToastCheck = UiTheme.Option("Cảnh báo Toast khi phát hiện PII");
            _piiToastCheck.Margin = new Padding(LogicalToDeviceUnits(16), 0, 0, LogicalToDeviceUnits(4));
            AddControl(_piiToastCheck, 4);

            _piiMaskHistoryCheck = UiTheme.Option("Tự động che dữ liệu nhạy cảm trong lịch sử");
            _piiMaskHistoryCheck.Margin = new Padding(LogicalToDeviceUnits(16), 0, 0, LogicalToDeviceUnits(10));
            AddControl(_piiMaskHistoryCheck, 10);

            _piiScanCheck.CheckedChanged += (s, e) =>
            {
                _piiToastCheck.Enabled = _piiScanCheck.Checked;
                _piiMaskHistoryCheck.Enabled = _piiScanCheck.Checked;
            };

            AddControl(UiTheme.Separator(this), 10);

            // ============================
            // 2. CHUYỂN SVG SANG PNG
            // ============================
            AddControl(UiTheme.SectionHeader("Chuyển đổi hình ảnh", UiTheme.Accent), 6);
            _svgToPngCheck = UiTheme.Option("Tự động chuyển SVG sang ảnh PNG khi copy");
            AddControl(_svgToPngCheck, 4);

            _svgTransparentCheck = UiTheme.Option("Giữ nền trong suốt cho ảnh PNG");
            _svgTransparentCheck.Margin = new Padding(LogicalToDeviceUnits(16), 0, 0, LogicalToDeviceUnits(10));
            AddControl(_svgTransparentCheck, 10);

            _svgToPngCheck.CheckedChanged += (s, e) =>
            {
                _svgTransparentCheck.Enabled = _svgToPngCheck.Checked;
            };

            AddControl(UiTheme.Separator(this), 10);

            // ============================
            // 3. TỪ KHÓA NHẠY CẢM RIÊNG
            // ============================
            AddControl(UiTheme.SectionHeader("Từ khóa nhạy cảm riêng", UiTheme.Accent), 4);
            _customKeywordsBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = UiTheme.Small,
                Height = LogicalToDeviceUnits(44),
                PlaceholderText = "Ví dụ: Tên dự án bí mật, mã nội bộ, mật khẩu wifi (mỗi từ một dòng)..."
            };
            AddControl(_customKeywordsBox, 10);

            AddControl(UiTheme.Separator(this), 10);

            // ============================
            // 4. HỆ THỐNG & LỊCH SỬ
            // ============================
            AddControl(UiTheme.SectionHeader("Hệ thống & Lịch sử", UiTheme.Accent), 6);
            _startWithWindowsCheck = UiTheme.Option("Khởi động cùng Windows (chạy ngầm dưới khay hệ thống)");
            AddControl(_startWithWindowsCheck, 4);

            _enableHistoryCheck = UiTheme.Option("Ghi nhớ lịch sử clipboard (lưu mã hóa bảo vệ qua DPAPI)");
            AddControl(_enableHistoryCheck, 4);

            _saveToDiskCheck = UiTheme.Option("Lưu lại lịch sử khi khởi động lại máy");
            _saveToDiskCheck.Margin = new Padding(LogicalToDeviceUnits(16), 0, 0, LogicalToDeviceUnits(8));
            AddControl(_saveToDiskCheck, 8);

            _enableHistoryCheck.CheckedChanged += (s, e) =>
            {
                _saveToDiskCheck.Enabled = _enableHistoryCheck.Checked;
            };

            content.Controls.Add(col);

            // ============================
            // FOOTER BAR
            // ============================
            Button saveButton = UiTheme.PrimaryButton("Lưu cấu hình");
            saveButton.Height = LogicalToDeviceUnits(28);
            saveButton.Click += OnSaveClicked;

            Button closeButton = UiTheme.SecondaryButton("Đóng");
            closeButton.Height = LogicalToDeviceUnits(28);
            closeButton.Click += (s, e) => Close();

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = LogicalToDeviceUnits(44),
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(8),
                                      LogicalToDeviceUnits(16), LogicalToDeviceUnits(8))
            };
            footer.Controls.Add(UiTheme.ButtonRow(closeButton, saveButton));

            var footerBorder = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            Controls.Add(content);
            Controls.Add(headerBorder);
            Controls.Add(header);
            Controls.Add(footerBorder);
            Controls.Add(footer);

            AcceptButton = saveButton;
            CancelButton = closeButton;

            LoadCurrentValues();
        }

        private void LoadCurrentValues()
        {
            _piiScanCheck.Checked = (AppConfig.Get("PII_SCAN_ENABLED") ?? "true").ToLower() != "false";
            _piiToastCheck.Checked = (AppConfig.Get("PII_TOAST_ENABLED") ?? "true").ToLower() != "false";
            _piiMaskHistoryCheck.Checked = (AppConfig.Get("PII_MASK_HISTORY") ?? "true").ToLower() != "false";
            _piiToastCheck.Enabled = _piiScanCheck.Checked;
            _piiMaskHistoryCheck.Enabled = _piiScanCheck.Checked;

            _customKeywordsBox.Text = (AppConfig.Get("PII_CUSTOM_KEYWORDS") ?? "").Replace("|", "\r\n");

            _svgToPngCheck.Checked = SvgPasteService.IsEnabled;
            _svgTransparentCheck.Checked = (AppConfig.Get("SVG_PNG_BACKGROUND") ?? "transparent").ToLower() != "white";
            _svgTransparentCheck.Enabled = _svgToPngCheck.Checked;

            _enableHistoryCheck.Checked = (AppConfig.Get("HISTORY_ENABLED") ?? "true").ToLower() != "false";
            _saveToDiskCheck.Checked = (AppConfig.Get("SAVE_HISTORY_TO_DISK") ?? "true").ToLower() != "false";
            _saveToDiskCheck.Enabled = _enableHistoryCheck.Checked;

            _startWithWindowsCheck.Checked = StartupManager.IsEnabled;
        }

        private void OnSaveClicked(object? sender, EventArgs e)
        {
            try
            {
                AppConfig.Set("PII_SCAN_ENABLED", _piiScanCheck.Checked ? "true" : "false");
                AppConfig.Set("PII_TOAST_ENABLED", _piiToastCheck.Checked ? "true" : "false");
                AppConfig.Set("PII_MASK_HISTORY", _piiMaskHistoryCheck.Checked ? "true" : "false");

                AppConfig.Set("SVG_TO_PNG_ENABLED", _svgToPngCheck.Checked ? "true" : "false");
                AppConfig.Set("SVG_PNG_BACKGROUND", _svgTransparentCheck.Checked ? "transparent" : "white");

                AppConfig.Set("HISTORY_ENABLED", _enableHistoryCheck.Checked ? "true" : "false");
                AppConfig.Set("SAVE_HISTORY_TO_DISK", _saveToDiskCheck.Checked ? "true" : "false");
                AppConfig.Set("ENCRYPT_HISTORY_DATA", "true"); // Luôn mã hóa DPAPI an toàn

                // Di trú dữ liệu lịch sử nếu cần
                HistoryManager.MigrateHistoryData(_historyList);

                // Lưu custom keywords
                string keywords = _customKeywordsBox.Text
                    .Replace("\r\n", "|")
                    .Replace("\n", "|")
                    .Replace("\r", "|");
                while (keywords.Contains("||")) keywords = keywords.Replace("||", "|");
                keywords = keywords.Trim('|');
                AppConfig.Set("PII_CUSTOM_KEYWORDS", keywords);

                if (_startWithWindowsCheck.Checked != StartupManager.IsEnabled &&
                    !StartupManager.TrySet(_startWithWindowsCheck.Checked, out string startupError))
                {
                    MessageBox.Show(
                        "Đã lưu cấu hình, nhưng không thể đổi mục khởi động cùng Windows:\n" + startupError,
                        "HapiCopi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Close();
                    return;
                }

                MessageBox.Show("Đã lưu cấu hình thành công!", "HapiCopi",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể lưu cấu hình: {ex.Message}", "Lỗi",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
