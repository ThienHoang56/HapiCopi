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
        private readonly CheckBox _svgToPngCheck;
        private readonly CheckBox _svgTransparentCheck;
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
            MinimumSize = new Size(LogicalToDeviceUnits(480), LogicalToDeviceUnits(450));
            ClientSize = new Size(LogicalToDeviceUnits(500), LogicalToDeviceUnits(520));
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
            // 1. CHUYỂN SVG SANG PNG
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
            // 2. HỆ THỐNG & LỊCH SỬ
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
            // 3. PHÍM TẮT HỆ THỐNG
            // ============================
            AddControl(UiTheme.SectionHeader("Phím tắt hệ thống", UiTheme.Accent), 6);

            AddControl(CreateHotkeyCard(
                "Tạm dừng / Tiếp tục",
                "Tạm ngừng hoặc tiếp tục tự động định dạng clipboard",
                "Ctrl + Alt + P"), 4);

            AddControl(CreateHotkeyCard(
                "Thoát ứng dụng",
                "Đóng sạch hoàn toàn ứng dụng và giải phóng bộ nhớ",
                "Ctrl + Alt + Q"), 4);

            AddControl(CreateHotkeyCard(
                "Soạn công thức Toán",
                "Mở bảng vẽ công thức toán và nhận diện LaTeX",
                "Ctrl + Alt"), 6);

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
                AppConfig.Set("SVG_TO_PNG_ENABLED", _svgToPngCheck.Checked ? "true" : "false");
                AppConfig.Set("SVG_PNG_BACKGROUND", _svgTransparentCheck.Checked ? "transparent" : "white");

                AppConfig.Set("HISTORY_ENABLED", _enableHistoryCheck.Checked ? "true" : "false");
                AppConfig.Set("SAVE_HISTORY_TO_DISK", _saveToDiskCheck.Checked ? "true" : "false");
                AppConfig.Set("ENCRYPT_HISTORY_DATA", "true"); // Luôn mã hóa DPAPI an toàn

                // Di trú dữ liệu lịch sử nếu cần
                HistoryManager.MigrateHistoryData(_historyList);

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
    }
}
