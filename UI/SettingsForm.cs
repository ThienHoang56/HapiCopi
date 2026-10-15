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
        private readonly CheckBox _checkKeysBox;
        private readonly CheckBox _checkCardsBox;
        private readonly CheckBox _checkCccdBox;
        private readonly CheckBox _checkPhoneBox;
        private readonly CheckBox _checkEmailBox;
        private readonly CheckBox _svgToPngCheck;
        private readonly CheckBox _svgTransparentCheck;
        private readonly TextBox _customKeywordsBox;
        private readonly Label _licenseStatusLabel;
        private readonly TextBox _licenseKeyBox;
        private readonly CheckBox _enableHistoryCheck;
        private readonly CheckBox _saveToDiskCheck;
        private readonly CheckBox _startWithWindowsCheck;
        private readonly System.Collections.Generic.List<ClipboardHistoryItem> _historyList;
        private readonly ToolTip _toolTip;

        public SettingsForm(System.Collections.Generic.List<ClipboardHistoryItem>? history = null)
        {
            _historyList = history ?? new System.Collections.Generic.List<ClipboardHistoryItem>();
            _toolTip = new ToolTip
            {
                InitialDelay = 200,
                ReshowDelay = 100,
                AutoPopDelay = 6000,
                ShowAlways = true
            };

            UiTheme.ApplyDialog(this, "HapiCopi — Cài đặt");
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // Kích thước chuẩn Mini Popup vừa vặn, không bị to bự chảng
            MinimumSize = new Size(LogicalToDeviceUnits(480), LogicalToDeviceUnits(560));
            ClientSize = new Size(LogicalToDeviceUnits(500), LogicalToDeviceUnits(680));
            KeyPreview = true;

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
            // 1. BẢO MẬT DỮ LIỆU (DLP)
            // ============================
            AddControl(UiTheme.SectionHeader("Bảo mật dữ liệu (DLP)", UiTheme.Accent), 6);
            _piiScanCheck = UiTheme.Option("Quét dữ liệu nhạy cảm");
            _toolTip.SetToolTip(_piiScanCheck, "Tự động phát hiện thông tin nhạy cảm khi sao chép");
            AddControl(_piiScanCheck, 4);

            _piiToastCheck = UiTheme.Option("Hiện thông báo cảnh báo");
            _piiToastCheck.Margin = new Padding(LogicalToDeviceUnits(16), 0, 0, LogicalToDeviceUnits(4));
            _toolTip.SetToolTip(_piiToastCheck, "Hiển thị thông báo góc màn hình khi phát hiện dữ liệu nhạy cảm");
            AddControl(_piiToastCheck, 4);

            _piiMaskHistoryCheck = UiTheme.Option("Ẩn dữ liệu nhạy cảm trong lịch sử");
            _piiMaskHistoryCheck.Margin = new Padding(LogicalToDeviceUnits(16), 0, 0, LogicalToDeviceUnits(8));
            _toolTip.SetToolTip(_piiMaskHistoryCheck, "Tự động làm mờ các thông tin nhạy cảm lưu trong lịch sử clipboard");
            AddControl(_piiMaskHistoryCheck, 8);

            var piiTypesHeader = UiTheme.Hint("Loại dữ liệu bảo vệ:", UiTheme.Accent);
            piiTypesHeader.Font = UiTheme.BodyStrong;
            piiTypesHeader.Margin = new Padding(LogicalToDeviceUnits(16), 0, 0, LogicalToDeviceUnits(4));
            AddControl(piiTypesHeader, 4);

            _checkKeysBox = UiTheme.Option("API Key, Token & Mật khẩu");
            _checkKeysBox.Margin = new Padding(LogicalToDeviceUnits(24), 0, 0, LogicalToDeviceUnits(3));
            _toolTip.SetToolTip(_checkKeysBox, "Phát hiện OpenAI, Google, AWS, GitHub key, Token và mật khẩu");
            AddControl(_checkKeysBox, 3);

            _checkCardsBox = UiTheme.Option("Thẻ ngân hàng & Thẻ tín dụng");
            _checkCardsBox.Margin = new Padding(LogicalToDeviceUnits(24), 0, 0, LogicalToDeviceUnits(3));
            _toolTip.SetToolTip(_checkCardsBox, "Phát hiện số thẻ Visa, MasterCard và thẻ ATM ngân hàng");
            AddControl(_checkCardsBox, 3);

            _checkCccdBox = UiTheme.Option("Căn cước công dân & CMND");
            _checkCccdBox.Margin = new Padding(LogicalToDeviceUnits(24), 0, 0, LogicalToDeviceUnits(3));
            _toolTip.SetToolTip(_checkCccdBox, "Phát hiện CCCD 12 số và CMND 9 số");
            AddControl(_checkCccdBox, 3);

            _checkPhoneBox = UiTheme.Option("Số điện thoại di động");
            _checkPhoneBox.Margin = new Padding(LogicalToDeviceUnits(24), 0, 0, LogicalToDeviceUnits(3));
            _toolTip.SetToolTip(_checkPhoneBox, "Phát hiện số điện thoại di động Việt Nam");
            AddControl(_checkPhoneBox, 3);

            _checkEmailBox = UiTheme.Option("Địa chỉ Email");
            _checkEmailBox.Margin = new Padding(LogicalToDeviceUnits(24), 0, 0, LogicalToDeviceUnits(10));
            _toolTip.SetToolTip(_checkEmailBox, "Phát hiện địa chỉ email cá nhân và công việc");
            AddControl(_checkEmailBox, 10);

            _piiScanCheck.CheckedChanged += (s, e) =>
            {
                bool enabled = _piiScanCheck.Checked;
                _piiToastCheck.Enabled = enabled;
                _piiMaskHistoryCheck.Enabled = enabled;
                _checkKeysBox.Enabled = enabled;
                _checkCardsBox.Enabled = enabled;
                _checkCccdBox.Enabled = enabled;
                _checkPhoneBox.Enabled = enabled;
                _checkEmailBox.Enabled = enabled;
            };

            AddControl(UiTheme.Separator(this), 10);

            // ============================
            // 2. CHUYỂN SVG SANG PNG
            // ============================
            AddControl(UiTheme.SectionHeader("Chuyển đổi hình ảnh", UiTheme.Accent), 6);
            _svgToPngCheck = UiTheme.Option("Tự động chuyển SVG sang PNG");
            _toolTip.SetToolTip(_svgToPngCheck, "Tự động chuyển vector SVG sang ảnh PNG khi copy để dán vào Word/PowerPoint");
            AddControl(_svgToPngCheck, 4);

            _svgTransparentCheck = UiTheme.Option("Giữ nền trong suốt cho PNG");
            _svgTransparentCheck.Margin = new Padding(LogicalToDeviceUnits(16), 0, 0, LogicalToDeviceUnits(10));
            _toolTip.SetToolTip(_svgTransparentCheck, "Giữ nền PNG trong suốt thay vì chèn nền màu trắng");
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
                PlaceholderText = "Tên dự án nội bộ, mã bí mật... (mỗi từ một dòng)"
            };
            _toolTip.SetToolTip(_customKeywordsBox, "Thêm các từ khóa riêng cần cảnh báo khi sao chép");
            AddControl(_customKeywordsBox, 10);

            AddControl(UiTheme.Separator(this), 10);

            // ============================
            // 4. BẢN QUYỀN HAPICOPI
            // ============================
            AddControl(UiTheme.SectionHeader("Bản quyền ứng dụng", UiTheme.Accent), 4);

            _licenseStatusLabel = new Label
            {
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.Accent,
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(4))
            };
            AddControl(_licenseStatusLabel, 4);

            var upgradeRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            upgradeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            upgradeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var machineHint = new Label
            {
                Text = $"Mã máy: {LicenseService.MachineId}",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            upgradeRow.Controls.Add(machineHint, 0, 0);
            AddControl(upgradeRow, 6);

            var licensePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            licensePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            licensePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _licenseKeyBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.Body,
                PlaceholderText = "Nhập mã kích hoạt (nếu có)"
            };
            _toolTip.SetToolTip(_licenseKeyBox, "Nhập mã kích hoạt thử nghiệm");

            var activateBtn = UiTheme.PrimaryButton("Kích hoạt");
            activateBtn.Margin = new Padding(LogicalToDeviceUnits(6), 0, 0, 0);
            _toolTip.SetToolTip(activateBtn, "Xác thực và kích hoạt bản quyền ngay lập tức");
            activateBtn.Click += (s, e) =>
            {
                string key = _licenseKeyBox.Text.Trim();
                var res = LicenseService.ActivateLicenseKey(key);
                if (res.success)
                {
                    MessageBox.Show(res.message, "HapiCopi Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _licenseKeyBox.Text = "";
                    UpdateLicenseStatusUI();
                }
                else
                {
                    MessageBox.Show(res.message, "Kích hoạt thất bại", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            licensePanel.Controls.Add(_licenseKeyBox, 0, 0);
            licensePanel.Controls.Add(activateBtn, 1, 0);
            AddControl(licensePanel, 10);

            AddControl(UiTheme.Separator(this), 10);

            // ============================
            // 5. HỆ THỐNG & LỊCH SỬ
            // ============================
            AddControl(UiTheme.SectionHeader("Hệ thống & Lịch sử", UiTheme.Accent), 6);
            _startWithWindowsCheck = UiTheme.Option("Khởi động cùng Windows");
            _toolTip.SetToolTip(_startWithWindowsCheck, "Tự động chạy ngầm dưới khay hệ thống khi bật máy");
            AddControl(_startWithWindowsCheck, 4);

            _enableHistoryCheck = UiTheme.Option("Lưu lịch sử clipboard");
            _toolTip.SetToolTip(_enableHistoryCheck, "Lưu lại nội dung sao chép, mã hóa an toàn qua Windows DPAPI");
            AddControl(_enableHistoryCheck, 4);

            _saveToDiskCheck = UiTheme.Option("Giữ lịch sử sau khi khởi động lại");
            _saveToDiskCheck.Margin = new Padding(LogicalToDeviceUnits(16), 0, 0, LogicalToDeviceUnits(8));
            _toolTip.SetToolTip(_saveToDiskCheck, "Lưu lịch sử vào ổ đĩa thay vì chỉ giữ tạm trong RAM");
            AddControl(_saveToDiskCheck, 8);

            _enableHistoryCheck.CheckedChanged += (s, e) =>
            {
                _saveToDiskCheck.Enabled = _enableHistoryCheck.Checked;
            };

            AddControl(UiTheme.Separator(this), 10);

            // ============================
            // 6. PHÍM TẮT HỆ THỐNG
            // ============================
            AddControl(UiTheme.SectionHeader("Phím tắt hệ thống", UiTheme.Accent), 6);

            AddControl(CreateHotkeyCard(
                "Tạm dừng / Tiếp tục",
                "Tạm ngừng hoặc tiếp tục quét bảo mật clipboard",
                "Ctrl + Alt + P"), 4);

            AddControl(CreateHotkeyCard(
                "Thoát ứng dụng",
                "Đóng sạch hoàn toàn ứng dụng và giải phóng bộ nhớ",
                "Ctrl + Alt + Q"), 4);

            AddControl(CreateHotkeyCard(
                "Soạn công thức Toán",
                "Mở bảng vẽ công thức toán và nhận diện LaTeX",
                "Ctrl + Alt + M"), 6);

            content.Controls.Add(col);

            // ============================
            // FOOTER BAR
            // ============================
            Button saveButton = UiTheme.PrimaryButton("Lưu cấu hình");
            saveButton.Click += OnSaveClicked;

            Button closeButton = UiTheme.SecondaryButton("Đóng");
            closeButton.Click += (s, e) => Close();

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = LogicalToDeviceUnits(58),
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(12),
                                      LogicalToDeviceUnits(16), LogicalToDeviceUnits(12))
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
            content.BringToFront();

            AcceptButton = saveButton;
            CancelButton = closeButton;

            LoadCurrentValues();
        }

        private void LoadCurrentValues()
        {
            _piiScanCheck.Checked = (AppConfig.Get("PII_SCAN_ENABLED") ?? "true").ToLower() != "false";
            _piiToastCheck.Checked = (AppConfig.Get("PII_TOAST_ENABLED") ?? "true").ToLower() != "false";
            _piiMaskHistoryCheck.Checked = (AppConfig.Get("PII_MASK_HISTORY") ?? "true").ToLower() != "false";

            _checkKeysBox.Checked = (AppConfig.Get("PII_CHECK_KEYS") ?? "true").ToLower() != "false";
            _checkCardsBox.Checked = (AppConfig.Get("PII_CHECK_CARDS") ?? "true").ToLower() != "false";
            _checkCccdBox.Checked = (AppConfig.Get("PII_CHECK_CCCD") ?? "true").ToLower() != "false";
            _checkPhoneBox.Checked = (AppConfig.Get("PII_CHECK_PHONE") ?? "true").ToLower() != "false";
            _checkEmailBox.Checked = (AppConfig.Get("PII_CHECK_EMAIL") ?? "true").ToLower() != "false";

            bool piiEnabled = _piiScanCheck.Checked;
            _piiToastCheck.Enabled = piiEnabled;
            _piiMaskHistoryCheck.Enabled = piiEnabled;
            _checkKeysBox.Enabled = piiEnabled;
            _checkCardsBox.Enabled = piiEnabled;
            _checkCccdBox.Enabled = piiEnabled;
            _checkPhoneBox.Enabled = piiEnabled;
            _checkEmailBox.Enabled = piiEnabled;

            _customKeywordsBox.Text = (AppConfig.Get("PII_CUSTOM_KEYWORDS") ?? "").Replace("|", "\r\n");
            UpdateLicenseStatusUI();

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

                AppConfig.Set("PII_CHECK_KEYS", _checkKeysBox.Checked ? "true" : "false");
                AppConfig.Set("PII_CHECK_CARDS", _checkCardsBox.Checked ? "true" : "false");
                AppConfig.Set("PII_CHECK_CCCD", _checkCccdBox.Checked ? "true" : "false");
                AppConfig.Set("PII_CHECK_PHONE", _checkPhoneBox.Checked ? "true" : "false");
                AppConfig.Set("PII_CHECK_EMAIL", _checkEmailBox.Checked ? "true" : "false");

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

        private Control CreateHotkeyCard(string title, string description, string badgeText)
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(LogicalToDeviceUnits(12), LogicalToDeviceUnits(6), LogicalToDeviceUnits(12), LogicalToDeviceUnits(6)),
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(4))
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                Text = title,
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var badge = new Label
            {
                Text = badgeText,
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.Accent,
                BackColor = UiTheme.AccentLight,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(LogicalToDeviceUnits(8), LogicalToDeviceUnits(3), LogicalToDeviceUnits(8), LogicalToDeviceUnits(3)),
                Margin = new Padding(LogicalToDeviceUnits(8), 0, 0, 0)
            };

            card.Controls.Add(titleLabel, 0, 0);
            card.Controls.Add(badge, 1, 0);

            _toolTip.SetToolTip(card, description);
            _toolTip.SetToolTip(titleLabel, description);
            _toolTip.SetToolTip(badge, description);

            return card;
        }

        private void UpdateLicenseStatusUI()
        {
            if (LicenseService.IsProLifetime)
            {
                _licenseStatusLabel.Text = "Trạng thái: Bản Pro Trọn Đời ✦ (Vô hạn Offline & Không quảng cáo)";
                _licenseStatusLabel.ForeColor = Color.FromArgb(16, 185, 129); // emerald
            }
            else if (LicenseService.HasActiveCloud)
            {
                _licenseStatusLabel.Text = $"Trạng thái: Gói Cloud AI (Còn {LicenseService.CloudQuotaRemaining}/1.000 lượt, hạn đến {LicenseService.CloudExpiryDate:dd/MM/yyyy})";
                _licenseStatusLabel.ForeColor = UiTheme.Accent;
            }
            else
            {
                _licenseStatusLabel.Text = $"Trạng thái: Bản Miễn phí (Còn {LicenseService.RemainingDailyFreeQuota}/50 lượt offline hôm nay)";
                _licenseStatusLabel.ForeColor = UiTheme.TextMuted;
            }
        }
    }
}
