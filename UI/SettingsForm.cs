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
        private readonly System.Collections.Generic.List<ClipboardHistoryItem> _historyList;

        public SettingsForm(System.Collections.Generic.List<ClipboardHistoryItem> history)
        {
            _historyList = history;
            Text = "HapiCopi - Cài đặt";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 800);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            int labelX = 20;
            int inputX = 20;
            int inputWidth = 480;
            int y = 18;

            // ============================
            // HEADER: Bảo mật Clipboard
            // ============================
            var header = new Label
            {
                Text = "🛡️ Bảo mật Clipboard (DLP)",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 204, 113),
                AutoSize = true,
                Location = new Point(labelX, y)
            };
            Controls.Add(header);
            y += 36;

            var descLabel = new Label
            {
                Text = "HapiCopi tự động quét thông tin nhạy cảm (API Key, mật khẩu, số điện thoại, email, số thẻ tín dụng, CCCD) trong clipboard để bảo vệ bạn khỏi rò rỉ dữ liệu.",
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(labelX, y),
                Size = new Size(inputWidth, 38)
            };
            Controls.Add(descLabel);
            y += 44;

            // ============================
            // CHECKBOXES
            // ============================
            _piiScanCheck = new CheckBox
            {
                Text = "Bật quét thông tin nhạy cảm (PII Scanner)",
                AutoSize = true,
                Location = new Point(inputX, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_piiScanCheck);
            y += 30;

            _piiToastCheck = new CheckBox
            {
                Text = "Hiển thị cảnh báo Toast khi phát hiện thông tin nhạy cảm",
                AutoSize = true,
                Location = new Point(inputX, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_piiToastCheck);
            y += 30;

            _piiMaskHistoryCheck = new CheckBox
            {
                Text = "Tự động che thông tin nhạy cảm trong Lịch sử Clipboard",
                AutoSize = true,
                Location = new Point(inputX, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_piiMaskHistoryCheck);
            y += 30;

            _mlFormatCheck = new CheckBox
            {
                Text = "Tự động cấu trúc văn bản thô bằng mô hình học máy (ML)",
                AutoSize = true,
                Location = new Point(inputX, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_mlFormatCheck);
            y += 40;

            // ============================
            // SVG → PNG
            // ============================
            var svgHeader = new Label
            {
                Text = "🖼️ Chuyển SVG sang PNG khi copy",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(155, 89, 182),
                AutoSize = true,
                Location = new Point(labelX, y)
            };
            Controls.Add(svgHeader);
            y += 26;

            var svgDesc = new Label
            {
                Text = "Copy file .svg hoặc markup SVG là HapiCopi rasterize ngay trên máy, dán thẳng vào Word/Google Docs/Slack ra ảnh. Không cần tải file lên web convert — tránh lộ sơ đồ, logo, bản vẽ nội bộ.",
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(labelX, y),
                Size = new Size(inputWidth, 46)
            };
            Controls.Add(svgDesc);
            y += 50;

            _svgToPngCheck = new CheckBox
            {
                Text = "Bật tự động chuyển SVG sang PNG",
                AutoSize = true,
                Location = new Point(inputX, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            _svgToPngCheck.CheckedChanged += (s, e) =>
            {
                _svgTransparentCheck.Enabled = _svgToPngCheck.Checked;
                _svgReplaceCheck.Enabled = _svgToPngCheck.Checked;
            };
            Controls.Add(_svgToPngCheck);
            y += 30;

            _svgTransparentCheck = new CheckBox
            {
                Text = "Giữ nền trong suốt (bỏ chọn = nền trắng)",
                AutoSize = true,
                Location = new Point(inputX, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_svgTransparentCheck);
            y += 30;

            _svgReplaceCheck = new CheckBox
            {
                Text = "Thay hẳn clipboard bằng PNG (bỏ chọn = giữ cả bản SVG gốc)",
                AutoSize = true,
                Location = new Point(inputX, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_svgReplaceCheck);
            y += 40;

            // ============================
            // CUSTOM KEYWORDS
            // ============================
            var keywordsHeader = new Label
            {
                Text = "Từ khóa nhạy cảm tùy chỉnh",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 126, 34),
                AutoSize = true,
                Location = new Point(labelX, y)
            };
            Controls.Add(keywordsHeader);
            y += 26;

            var keywordsDesc = new Label
            {
                Text = "Nhập các từ khóa mà bạn muốn HapiCopi cảnh báo khi phát hiện trong clipboard, mỗi từ một dòng. Ví dụ: tên dự án bí mật, mã nội bộ...",
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(labelX, y),
                Size = new Size(inputWidth, 30)
            };
            Controls.Add(keywordsDesc);
            y += 34;

            _customKeywordsBox = new TextBox
            {
                Location = new Point(inputX, y),
                Width = inputWidth,
                Height = 80,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                PlaceholderText = "Ví dụ:\nProject Phoenix\nKH-VIP-2026\nmật khẩu wifi công ty"
            };
            Controls.Add(_customKeywordsBox);
            y += 90;

            // ============================
            // HEADER: Cấu hình Lịch sử Clipboard
            // ============================
            y += 10;
            var historyHeader = new Label
            {
                Text = "📋 Cấu hình Lịch sử Clipboard",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 152, 219),
                AutoSize = true,
                Location = new Point(labelX, y)
            };
            Controls.Add(historyHeader);
            y += 26;

            _enableHistoryCheck = new CheckBox
            {
                Text = "Ghi nhớ lịch sử clipboard (Tối đa 30 mục)",
                AutoSize = true,
                Location = new Point(inputX, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            _enableHistoryCheck.CheckedChanged += (s, e) => {
                _saveToDiskCheck.Enabled = _enableHistoryCheck.Checked;
                _encryptHistoryCheck.Enabled = _enableHistoryCheck.Checked && _saveToDiskCheck.Checked;
            };
            Controls.Add(_enableHistoryCheck);
            y += 30;

            _saveToDiskCheck = new CheckBox
            {
                Text = "Lưu lịch sử vào ổ đĩa để không bị mất khi khởi động lại",
                AutoSize = true,
                Location = new Point(inputX, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            _saveToDiskCheck.CheckedChanged += (s, e) => {
                _encryptHistoryCheck.Enabled = _enableHistoryCheck.Checked && _saveToDiskCheck.Checked;
            };
            Controls.Add(_saveToDiskCheck);
            y += 30;

            _encryptHistoryCheck = new CheckBox
            {
                Text = "Mã hóa dữ liệu lịch sử trên ổ đĩa (Khuyên dùng) 🛡️",
                AutoSize = true,
                Location = new Point(inputX, y),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_encryptHistoryCheck);
            y += 40;

            // ============================
            // BUTTONS
            // ============================
            var saveButton = new Button
            {
                Text = "Lưu",
                Width = 110,
                Height = 32,
                Location = new Point(ClientSize.Width - 250, ClientSize.Height - 46),
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            saveButton.FlatAppearance.BorderSize = 0;
            saveButton.Click += OnSaveClicked;
            Controls.Add(saveButton);

            var cancelButton = new Button
            {
                Text = "Đóng",
                Width = 110,
                Height = 32,
                Location = new Point(ClientSize.Width - 130, ClientSize.Height - 46),
                FlatStyle = FlatStyle.Flat
            };
            cancelButton.Click += (s, e) => Close();
            Controls.Add(cancelButton);

            AcceptButton = saveButton;
            CancelButton = cancelButton;

            LoadCurrentValues();
        }

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
