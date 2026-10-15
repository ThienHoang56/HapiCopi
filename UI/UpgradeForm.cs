using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Hộp thoại Nâng cấp Bản quyền & Hướng dẫn Thanh toán VietQR.
    /// Thiết kế chuẩn mực, minh bạch, đảm bảo Z-Order luôn hiển thị trên cùng.
    /// </summary>
    public class UpgradeForm : Form
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private readonly Label _statusLabel;
        private readonly TextBox _machineIdBox;
        private readonly RadioButton _radioPro;
        private readonly RadioButton _radioCloud;
        private readonly PictureBox _qrPictureBox;
        private readonly Label _qrInfoLabel;
        private readonly TextBox _keyBox;
        private readonly Button _activateBtn;

        public UpgradeForm()
        {
            UiTheme.ApplyDialog(this, "HapiCopi — Nâng cấp Bản quyền");
            ShowInTaskbar = false;
            TopMost = true; // Luôn hiển thị trên cùng layer, không bị MathEditorForm che khuất
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(LogicalToDeviceUnits(500), LogicalToDeviceUnits(640));
            KeyPreview = true;

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            // 1. Header Bar
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = LogicalToDeviceUnits(48),
                Padding = new Padding(LogicalToDeviceUnits(18), LogicalToDeviceUnits(12), LogicalToDeviceUnits(18), 0),
                BackColor = UiTheme.Surface
            };
            var title = UiTheme.PageTitleLabel("Nâng cấp Bản quyền HapiCopi");
            title.Dock = DockStyle.Fill;
            header.Controls.Add(title);

            var headerBorder = new Panel
            {
                Dock = DockStyle.Top,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            // 2. Body Panel
            var content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Background,
                Padding = new Padding(LogicalToDeviceUnits(18), LogicalToDeviceUnits(12),
                                      LogicalToDeviceUnits(18), LogicalToDeviceUnits(10)),
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

            // Current Status
            _statusLabel = new Label
            {
                Font = UiTheme.BodyStrong,
                ForeColor = LicenseService.IsProLifetime ? Color.FromArgb(16, 185, 129) : UiTheme.Accent,
                AutoSize = true,
                Text = GetCurrentStatusText()
            };
            AddControl(_statusLabel, 8);

            // Machine ID Row
            AddControl(UiTheme.SectionHeader("1. Mã thiết bị của bạn", UiTheme.Accent), 2);
            var machineIdPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            machineIdPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            machineIdPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _machineIdBox = new TextBox
            {
                Text = LicenseService.MachineId,
                ReadOnly = true,
                Font = UiTheme.BodyStrong,
                BackColor = UiTheme.Surface,
                Dock = DockStyle.Fill,
                TextAlign = HorizontalAlignment.Center
            };
            var copyMachineBtn = UiTheme.SecondaryButton("Sao chép");
            copyMachineBtn.TextAlign = ContentAlignment.MiddleCenter;
            copyMachineBtn.Margin = new Padding(LogicalToDeviceUnits(6), 0, 0, 0);
            copyMachineBtn.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(LicenseService.MachineId);
                    MessageBox.Show($"Đã sao chép mã máy: {LicenseService.MachineId}", "HapiCopi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch { }
            };

            machineIdPanel.Controls.Add(_machineIdBox, 0, 0);
            machineIdPanel.Controls.Add(copyMachineBtn, 1, 0);
            AddControl(machineIdPanel, 10);

            // Plan Selection
            AddControl(UiTheme.SectionHeader("2. Chọn gói dịch vụ", UiTheme.Accent), 4);

            bool alreadyPro = LicenseService.IsProLifetime;

            _radioPro = new RadioButton
            {
                Text = alreadyPro 
                    ? "✓ Bản Pro Trọn Đời — Đã kích hoạt trên máy này" 
                    : $"Bản Pro Trọn Đời — {PaymentConfig.PriceProLifetime:N0} VNĐ (Thanh toán 1 lần duy nhất)",
                Font = UiTheme.BodyStrong,
                ForeColor = alreadyPro ? Color.FromArgb(16, 185, 129) : UiTheme.TextPrimary,
                AutoSize = true,
                Enabled = !alreadyPro,
                Checked = !alreadyPro
            };
            _radioPro.CheckedChanged += (s, e) => { if (_radioPro.Checked) UpdatePaymentInfo(); };
            AddControl(_radioPro, 2);

            var proHint = UiTheme.Hint(
                alreadyPro 
                    ? "• Bạn đã sở hữu bản quyền Pro vĩnh viễn cho máy tính này."
                    : "• Dùng Offline không giới hạn trọn đời. Chỉnh sửa trực tiếp công thức trong Word. Tặng kèm 1 tháng Cloud.",
                UiTheme.TextMuted);
            AddControl(proHint, 6);

            _radioCloud = new RadioButton
            {
                Text = alreadyPro 
                    ? $"Gói Cloud AI Tốc độ cao — {PaymentConfig.PriceCloudMonthly:N0} VNĐ / tháng (Gia hạn 1.000 lượt)" 
                    : $"Gói Cloud AI Tốc độ cao — {PaymentConfig.PriceCloudMonthly:N0} VNĐ / tháng",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Checked = alreadyPro
            };
            _radioCloud.CheckedChanged += (s, e) => { if (_radioCloud.Checked) UpdatePaymentInfo(); };
            AddControl(_radioCloud, 2);

            var cloudHint = UiTheme.Hint("• 1.000 lượt nhận diện với đám mây trong 30 ngày.", UiTheme.TextMuted);
            AddControl(cloudHint, 10);

            // VietQR Box
            AddControl(UiTheme.SectionHeader("3. Chuyển khoản VietQR tự động", UiTheme.Accent), 4);
            var qrCard = new Panel
            {
                Dock = DockStyle.Top,
                Height = LogicalToDeviceUnits(170),
                BackColor = UiTheme.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(LogicalToDeviceUnits(8))
            };

            _qrPictureBox = new PictureBox
            {
                Dock = DockStyle.Left,
                Width = LogicalToDeviceUnits(150),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White
            };

            _qrInfoLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextPrimary,
                Padding = new Padding(LogicalToDeviceUnits(10), LogicalToDeviceUnits(4), 0, 0)
            };

            qrCard.Controls.Add(_qrInfoLabel);
            qrCard.Controls.Add(_qrPictureBox);
            AddControl(qrCard, 10);

            // Key Activation
            AddControl(UiTheme.SectionHeader("4. Kích hoạt License Key", UiTheme.Accent), 4);
            var keyPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _keyBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.Body,
                PlaceholderText = "Dán mã License Key (HAPI-PRO-...)"
            };

            _activateBtn = UiTheme.PrimaryButton("Kích hoạt");
            _activateBtn.TextAlign = ContentAlignment.MiddleCenter;
            _activateBtn.Margin = new Padding(LogicalToDeviceUnits(6), 0, 0, 0);
            _activateBtn.Click += OnActivateClicked;

            keyPanel.Controls.Add(_keyBox, 0, 0);
            keyPanel.Controls.Add(_activateBtn, 1, 0);
            AddControl(keyPanel, 8);

            content.Controls.Add(col);

            // 3. Footer Bar
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = LogicalToDeviceUnits(56),
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(10),
                                      LogicalToDeviceUnits(16), LogicalToDeviceUnits(10))
            };
            var closeBtn = UiTheme.SecondaryButton("Đóng");
            closeBtn.TextAlign = ContentAlignment.MiddleCenter;
            closeBtn.Click += (s, e) => Close();

            var btnRow = UiTheme.ButtonRow(closeBtn);
            footer.Controls.Add(btnRow);

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

            UpdatePaymentInfo();
        }

        private static string GetCurrentStatusText()
        {
            if (LicenseService.IsProLifetime)
            {
                return "Trạng thái: Bản Pro Trọn Đời (Vô hạn Offline & Chỉnh sửa Word)";
            }
            if (LicenseService.HasActiveCloud)
            {
                return $"Trạng thái: Gói Cloud AI (Còn {LicenseService.CloudQuotaRemaining}/1.000 lượt, hạn đến {LicenseService.CloudExpiryDate:dd/MM/yyyy})";
            }
            return $"Trạng thái: Bản Miễn phí (Còn {LicenseService.RemainingDailyFreeQuota}/{LicenseService.EffectiveDailyFreeQuota} lượt offline hôm nay)";
        }

        private void UpdatePaymentInfo()
        {
            int amount = _radioPro.Checked ? PaymentConfig.PriceProLifetime : PaymentConfig.PriceCloudMonthly;
            string planName = _radioPro.Checked ? "Bản quyền Pro Trọn Đời" : "Gói Cloud AI (1 tháng)";
            string machineId = LicenseService.MachineId;
            string orderCode = $"HAPI{machineId}";

            _qrInfoLabel.Text =
                $"Gói: {planName}\n" +
                $"Số tiền: {amount:N0} VNĐ\n" +
                $"Ngân hàng: {PaymentConfig.BankName} — STK: {PaymentConfig.BankAccount}\n" +
                $"Chủ tài khoản: {PaymentConfig.AccountHolder}\n" +
                $"Nội dung CK: {orderCode}\n\n" +
                "Quét mã QR bằng App Ngân hàng hoặc MoMo để chuyển khoản nhanh.\nMã máy trong nội dung dùng để cấp bản quyền chính xác cho thiết bị này.";

            // Tải mã VietQR động chuẩn SePay
            string qrUrl = $"https://qr.sepay.vn/img?acc={PaymentConfig.BankAccount}&bank={PaymentConfig.BankName}&amount={amount}&des={orderCode}";
            _ = LoadQrAsync(qrUrl);
        }

        private async Task LoadQrAsync(string url)
        {
            try
            {
                byte[] data = await _httpClient.GetByteArrayAsync(url);
                using var ms = new MemoryStream(data);
                var img = Image.FromStream(ms);

                if (!IsDisposed && _qrPictureBox != null)
                {
                    BeginInvoke(new Action(() =>
                    {
                        var old = _qrPictureBox.Image;
                        _qrPictureBox.Image = img;
                        old?.Dispose();
                    }));
                }
            }
            catch
            {
                // Fallback nếu không có mạng
            }
        }

        private void OnActivateClicked(object? sender, EventArgs e)
        {
            string key = _keyBox.Text.Trim();
            var res = LicenseService.ActivateLicenseKey(key);
            if (res.success)
            {
                MessageBox.Show(res.message, "HapiCopi Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _keyBox.Text = "";
                _statusLabel.Text = GetCurrentStatusText();
                _statusLabel.ForeColor = Color.FromArgb(16, 185, 129);

                if (LicenseService.IsProLifetime)
                {
                    _radioPro.Text = "✓ Bản Pro Trọn Đời — Đã kích hoạt trên máy này";
                    _radioPro.Enabled = false;
                    _radioCloud.Checked = true;
                }

                DialogResult = DialogResult.OK;
            }
            else
            {
                MessageBox.Show(res.message, "Kích hoạt thất bại", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
