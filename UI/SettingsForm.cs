using System;
using System.Drawing;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Cửa sổ Cài đặt cho HapiCopi.
    /// Cho phép người dùng cấu hình tính năng bảo mật clipboard (DLP).
    /// Cấu hình được lưu vào %APPDATA%\HapiCopi\config.env.
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly CheckBox _piiScanCheck;
        private readonly CheckBox _piiToastCheck;
        private readonly CheckBox _piiMaskHistoryCheck;
        private readonly CheckBox _mlFormatCheck;
        private readonly CheckBox _svgToPngCheck;
        private readonly CheckBox _svgTransparentCheck;
        private readonly CheckBox _svgReplaceCheck;
        private readonly TextBox _customKeywordsBox;
        private readonly CheckBox _enableHistoryCheck;
        private readonly CheckBox _saveToDiskCheck;
        private readonly CheckBox _encryptHistoryCheck;
        private readonly CheckBox _startWithWindowsCheck;
        private readonly System.Collections.Generic.List<ClipboardHistoryItem> _historyList;

        public SettingsForm(System.Collections.Generic.List<ClipboardHistoryItem> history)
        {
            _historyList = history;

            UiTheme.ApplyDialog(this, "HapiCopi — Cài đặt");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;

            // Nội dung dài hơn màn hình thì cuộn, chứ không cắt cụt.
            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.Background,
                Padding = new Padding(LogicalToDeviceUnits(22), LogicalToDeviceUnits(18),
                                      LogicalToDeviceUnits(22), LogicalToDeviceUnits(10))
            };

            TableLayoutPanel content = UiTheme.Column();

            content.AddRow(UiTheme.PageTitleLabel("Cài đặt"), 16);

            // ============================
            // BẢO MẬT CLIPBOARD (DLP)
            // ============================
            content.AddRow(UiTheme.SectionHeader("Bảo mật Clipboard (DLP)", UiTheme.Success), 4);
            content.AddRow(UiTheme.Description(
                "HapiCopi tự động quét thông tin nhạy cảm (API Key, mật khẩu, số điện thoại, email, " +
                "số thẻ tín dụng, CCCD) trong clipboard để bảo vệ bạn khỏi rò rỉ dữ liệu."), 12);

            _piiScanCheck = content.AddRow(UiTheme.Option("Bật quét thông tin nhạy cảm (PII Scanner)"), 6);
            _piiToastCheck = content.AddRow(UiTheme.Option("Hiển thị cảnh báo Toast khi phát hiện thông tin nhạy cảm"), 6);
            _piiMaskHistoryCheck = content.AddRow(UiTheme.Option("Tự động che thông tin nhạy cảm trong Lịch sử Clipboard"), 6);
            _mlFormatCheck = content.AddRow(UiTheme.Option("Tự động cấu trúc văn bản thô bằng mô hình học máy (ML)"), 18);

            content.AddRow(UiTheme.Separator(this), 18);

            // ============================
            // SVG → PNG
            // ============================
            content.AddRow(UiTheme.SectionHeader("Chuyển SVG sang PNG khi copy", UiTheme.Accent), 4);
            content.AddRow(UiTheme.Description(
                "Copy file .svg hoặc markup SVG là HapiCopi rasterize ngay trên máy, dán thẳng vào " +
                "Word/Google Docs/Slack ra ảnh. Không cần tải file lên web convert — tránh lộ sơ đồ, " +
                "logo, bản vẽ nội bộ."), 12);

            _svgToPngCheck = content.AddRow(UiTheme.Option("Bật tự động chuyển SVG sang PNG"), 6);
            _svgTransparentCheck = content.AddRow(UiTheme.Option("Giữ nền trong suốt (bỏ chọn = nền trắng)"), 6);
            _svgReplaceCheck = content.AddRow(UiTheme.Option("Thay hẳn clipboard bằng PNG (bỏ chọn = giữ cả bản SVG gốc)"), 18);

            _svgToPngCheck.CheckedChanged += (s, e) =>
            {
                _svgTransparentCheck.Enabled = _svgToPngCheck.Checked;
                _svgReplaceCheck.Enabled = _svgToPngCheck.Checked;
            };

            content.AddRow(UiTheme.Separator(this), 18);

            // ============================
            // TỪ KHÓA NHẠY CẢM TÙY CHỈNH
            // ============================
            content.AddRow(UiTheme.SectionHeader("Từ khóa nhạy cảm tùy chỉnh", UiTheme.Warning), 4);
            content.AddRow(UiTheme.Description(
                "Nhập các từ khóa mà bạn muốn HapiCopi cảnh báo khi phát hiện trong clipboard, " +
                "mỗi từ một dòng. Ví dụ: tên dự án bí mật, mã nội bộ..."), 10);

            _customKeywordsBox = content.AddRow(new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = UiTheme.Body,
                // Chiều cao tính theo chiều cao dòng chữ thật nên không bị cụt khi Windows phóng to chữ.
                Height = LogicalToDeviceUnits(72),
                PlaceholderText = "Ví dụ:\nProject Phoenix\nKH-VIP-2026\nmật khẩu wifi công ty"
            }, 18);

            content.AddRow(UiTheme.Separator(this), 18);

            // ============================
            // KHỞI ĐỘNG
            // ============================
            content.AddRow(UiTheme.SectionHeader("Khởi động", UiTheme.Accent), 10);
            _startWithWindowsCheck = content.AddRow(UiTheme.Option("Chạy HapiCopi cùng Windows"), 4);
            content.AddRow(UiTheme.Description(
                "App chạy ẩn ở khay hệ thống. Tắt mục này thì mỗi lần bật máy bạn phải tự mở HapiCopi."), 12);

            content.AddRow(UiTheme.Separator(this), 18);

            // ============================
            // LỊCH SỬ CLIPBOARD
            // ============================
            content.AddRow(UiTheme.SectionHeader("Lịch sử Clipboard", UiTheme.Accent), 10);

            _enableHistoryCheck = content.AddRow(UiTheme.Option("Ghi nhớ lịch sử clipboard (tối đa 30 mục)"), 6);
            _saveToDiskCheck = content.AddRow(UiTheme.Option("Lưu lịch sử vào ổ đĩa để không bị mất khi khởi động lại"), 6);
            _encryptHistoryCheck = content.AddRow(UiTheme.Option("Mã hóa dữ liệu lịch sử trên ổ đĩa (khuyên dùng)"), 8);

            _enableHistoryCheck.CheckedChanged += (s, e) =>
            {
                _saveToDiskCheck.Enabled = _enableHistoryCheck.Checked;
                _encryptHistoryCheck.Enabled = _enableHistoryCheck.Checked && _saveToDiskCheck.Checked;
            };
            _saveToDiskCheck.CheckedChanged += (s, e) =>
            {
                _encryptHistoryCheck.Enabled = _enableHistoryCheck.Checked && _saveToDiskCheck.Checked;
            };

            scrollHost.Controls.Add(content);

            // ============================
            // HÀNG NÚT
            // ============================
            Button saveButton = UiTheme.PrimaryButton("Lưu");
            saveButton.Click += OnSaveClicked;

            Button closeButton = UiTheme.SecondaryButton("Đóng");
            closeButton.Click += (s, e) => Close();

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(22), LogicalToDeviceUnits(12),
                                      LogicalToDeviceUnits(22), LogicalToDeviceUnits(12))
            };
            footer.Controls.Add(UiTheme.ButtonRow(closeButton, saveButton));

            var footerBorder = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            Controls.Add(scrollHost);
            Controls.Add(footerBorder);
            Controls.Add(footer);

            AcceptButton = saveButton;
            CancelButton = closeButton;

            LoadCurrentValues();

            // Chốt bề rộng chữ trước khi đo, rồi bám theo mỗi lần người dùng kéo giãn cửa sổ.
            int contentWidth = LogicalToDeviceUnits(DesignWidthDip) - scrollHost.Padding.Horizontal;
            content.MaximumSize = new Size(contentWidth, 0);
            UiTheme.ApplyWrapWidth(content, contentWidth);
            UiTheme.FitToContent(this, content, minWidthDip: DesignWidthDip, minHeightDip: 480);
            // FitToContent chỉ đo cột nội dung; cộng thêm footer kẻ không bị che mất dòng cuối.
            int extra = footer.PreferredSize.Height + footerBorder.Height;
            Rectangle work = Screen.FromControl(this).WorkingArea;
            Height = Math.Min(Height + extra, work.Height - LogicalToDeviceUnits(40));

            scrollHost.ClientSizeChanged += (s, e) =>
            {
                int available = scrollHost.ClientSize.Width - scrollHost.Padding.Horizontal;
                UiTheme.ApplyWrapWidth(content, available);
            };
        }

        /// <summary>Bề rộng thiết kế tính theo đơn vị 96 DPI; Windows tự nhân lên khi phóng to chữ.</summary>
        private const int DesignWidthDip = 560;

        private void LoadCurrentValues()
        {
            _piiScanCheck.Checked = (AppConfig.Get("PII_SCAN_ENABLED") ?? "true").ToLower() != "false";
            _piiToastCheck.Checked = (AppConfig.Get("PII_TOAST_ENABLED") ?? "true").ToLower() != "false";
            _piiMaskHistoryCheck.Checked = (AppConfig.Get("PII_MASK_HISTORY") ?? "true").ToLower() != "false";
            _mlFormatCheck.Checked = (AppConfig.Get("ML_FORMAT_ENABLED") ?? "true").ToLower() != "false";
            _customKeywordsBox.Text = (AppConfig.Get("PII_CUSTOM_KEYWORDS") ?? "").Replace("|", "\r\n");

            _svgToPngCheck.Checked = SvgPasteService.IsEnabled;
            _svgTransparentCheck.Checked = (AppConfig.Get("SVG_PNG_BACKGROUND") ?? "transparent").ToLower() != "white";
            _svgReplaceCheck.Checked = SvgPasteService.ReplaceOriginal;
            _svgTransparentCheck.Enabled = _svgToPngCheck.Checked;
            _svgReplaceCheck.Enabled = _svgToPngCheck.Checked;

            _enableHistoryCheck.Checked = (AppConfig.Get("HISTORY_ENABLED") ?? "true").ToLower() != "false";
            _saveToDiskCheck.Checked = (AppConfig.Get("SAVE_HISTORY_TO_DISK") ?? "true").ToLower() != "false";
            _encryptHistoryCheck.Checked = (AppConfig.Get("ENCRYPT_HISTORY_DATA") ?? "true").ToLower() != "false";

            _saveToDiskCheck.Enabled = _enableHistoryCheck.Checked;
            _encryptHistoryCheck.Enabled = _enableHistoryCheck.Checked && _saveToDiskCheck.Checked;

            _startWithWindowsCheck.Checked = StartupManager.IsEnabled;
        }

        private void OnSaveClicked(object? sender, EventArgs e)
        {
            try
            {
                AppConfig.Set("PII_SCAN_ENABLED", _piiScanCheck.Checked ? "true" : "false");
                AppConfig.Set("PII_TOAST_ENABLED", _piiToastCheck.Checked ? "true" : "false");
                AppConfig.Set("PII_MASK_HISTORY", _piiMaskHistoryCheck.Checked ? "true" : "false");
                AppConfig.Set("ML_FORMAT_ENABLED", _mlFormatCheck.Checked ? "true" : "false");

                AppConfig.Set("SVG_TO_PNG_ENABLED", _svgToPngCheck.Checked ? "true" : "false");
                AppConfig.Set("SVG_PNG_BACKGROUND", _svgTransparentCheck.Checked ? "transparent" : "white");
                AppConfig.Set("SVG_PASTE_MODE", _svgReplaceCheck.Checked ? "replace" : "augment");

                AppConfig.Set("HISTORY_ENABLED", _enableHistoryCheck.Checked ? "true" : "false");
                AppConfig.Set("SAVE_HISTORY_TO_DISK", _saveToDiskCheck.Checked ? "true" : "false");
                AppConfig.Set("ENCRYPT_HISTORY_DATA", _encryptHistoryCheck.Checked ? "true" : "false");

                // Gọi di trú dữ liệu nóng (Hot-Migration)
                HistoryManager.MigrateHistoryData(_historyList);

                // Lưu custom keywords dưới dạng pipe-separated (| thay cho newline)
                string keywords = _customKeywordsBox.Text
                    .Replace("\r\n", "|")
                    .Replace("\n", "|")
                    .Replace("\r", "|");
                // Loại bỏ các pipe trống liên tiếp
                while (keywords.Contains("||")) keywords = keywords.Replace("||", "|");
                keywords = keywords.Trim('|');
                AppConfig.Set("PII_CUSTOM_KEYWORDS", keywords);

                if (_startWithWindowsCheck.Checked != StartupManager.IsEnabled &&
                    !StartupManager.TrySet(_startWithWindowsCheck.Checked, out string startupError))
                {
                    MessageBox.Show(
                        "Đã lưu cấu hình, nhưng không đổi được mục chạy cùng Windows:\n" + startupError,
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
