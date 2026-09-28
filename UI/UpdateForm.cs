using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Cửa sổ quản lý phiên bản và cập nhật phần mềm chuẩn UiTheme.
    /// Hỗ trợ kiểm tra từ nguồn cập nhật (LAN/Folder/Web) và cài đặt trực tiếp từ file bộ cài (.exe).
    /// </summary>
    public class UpdateForm : Form
    {
        private readonly Label _statusHeaderLabel;
        private readonly Label _statusSubLabel;
        private readonly Label _feedInfoLabel;
        private readonly Button _changeFeedButton;
        private readonly Label _warningLabel;
        private readonly ListView _releaseList;
        private readonly Label _notesLabel;
        private readonly ProgressBar _progress;
        private readonly Label _statusLabel;

        private readonly Button _actionButton;
        private readonly Button _fileUpdateButton;
        private readonly Button _rollbackButton;
        private readonly Button _refreshButton;
        private readonly Button _closeButton;

        private UpdateCheckResult? _result;
        private bool _isBusy;

        public UpdateForm()
        {
            UiTheme.ApplyDialog(this, "HapiCopi — Phiên bản và cập nhật");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(LogicalToDeviceUnits(22), LogicalToDeviceUnits(16),
                                      LogicalToDeviceUnits(22), LogicalToDeviceUnits(10)),
                BackColor = UiTheme.Background
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            int row = 0;
            void AddAuto(Control control, int bottomGap = 8)
            {
                control.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(bottomGap));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(control, 0, row++);
            }

            // 1. Tiêu đề trang
            AddAuto(UiTheme.PageTitleLabel("Phiên bản & Cập nhật"), 12);

            // 2. Thẻ trạng thái hiện tại (Status Card)
            var statusCard = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(12),
                                      LogicalToDeviceUnits(16), LogicalToDeviceUnits(12))
            };
            statusCard.Paint += (s, e) =>
            {
                using var pen = new Pen(UiTheme.Border, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, statusCard.Width - 1, statusCard.Height - 1);
            };

            var cardLayout = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _statusHeaderLabel = new Label
            {
                Text = $"Đang dùng: v{UpdateManager.CurrentVersionText}",
                Font = UiTheme.SectionTitle,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                UseMnemonic = false,
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(4))
            };
            cardLayout.Controls.Add(_statusHeaderLabel);

            _statusSubLabel = UiTheme.Description("Đang kiểm tra phiên bản mới...");
            _statusSubLabel.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(8));
            cardLayout.Controls.Add(_statusSubLabel);

            var feedRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };

            _feedInfoLabel = new Label
            {
                Text = $"Nguồn: {UpdateManager.Feed}",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                UseMnemonic = false,
                Margin = new Padding(0, LogicalToDeviceUnits(4), LogicalToDeviceUnits(12), 0)
            };
            feedRow.Controls.Add(_feedInfoLabel);

            _changeFeedButton = UiTheme.SecondaryButton("Đổi nguồn...");
            _changeFeedButton.Font = UiTheme.Small;
            _changeFeedButton.Padding = new Padding(LogicalToDeviceUnits(8), LogicalToDeviceUnits(3),
                                                    LogicalToDeviceUnits(8), LogicalToDeviceUnits(3));
            _changeFeedButton.MinimumSize = new Size(LogicalToDeviceUnits(70), LogicalToDeviceUnits(24));
            _changeFeedButton.Click += OnChangeFeedClicked;
            feedRow.Controls.Add(_changeFeedButton);

            cardLayout.Controls.Add(feedRow);
            statusCard.Controls.Add(cardLayout);
            AddAuto(statusCard, 10);

            // 3. Cảnh báo chạy bản Portable (chưa qua bộ cài)
            _warningLabel = UiTheme.Hint(
                "Lưu ý: Bạn đang chạy bản Portable (chưa qua bộ cài Inno). " +
                "Khi cập nhật, hệ thống sẽ tiến hành cài đặt ứng dụng vào hệ thống.",
                UiTheme.Warning);
            _warningLabel.Visible = !UpdateManager.IsRunningInstalledCopy;
            AddAuto(_warningLabel, 8);

            // 4. Danh sách phiên bản
            AddAuto(UiTheme.SectionHeader("Lịch sử phiên bản phát hành:", UiTheme.TextPrimary), 4);

            _releaseList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                GridLines = false,
                Font = UiTheme.Body,
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(8))
            };
            _releaseList.Columns.Add("Phiên bản", 110);
            _releaseList.Columns.Add("Kênh", 110);
            _releaseList.Columns.Add("Ngày phát hành", 130);
            _releaseList.Columns.Add("Trạng thái", 210);
            _releaseList.SelectedIndexChanged += (s, e) => UpdateActionButton();

            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(_releaseList, 0, row++);

            // 5. Ghi chú thay đổi (Changelog)
            _notesLabel = UiTheme.Description("");
            AddAuto(_notesLabel, 6);

            // 6. Thanh tiến trình
            _progress = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Height = LogicalToDeviceUnits(12),
                Visible = false
            };
            AddAuto(_progress, 6);

            // 7. Nhãn trạng thái tiến trình
            _statusLabel = UiTheme.Description("Đang đọc danh sách phiên bản...");
            AddAuto(_statusLabel, 4);

            // 8. Các nút hành động
            _actionButton = UiTheme.PrimaryButton("Cài bản này");
            _actionButton.Enabled = false;
            _actionButton.Click += OnActionClicked;

            _fileUpdateButton = UiTheme.SecondaryButton("Cập nhật từ file .exe...");
            _fileUpdateButton.Click += OnFileUpdateClicked;

            _rollbackButton = UiTheme.SecondaryButton("Quay về bản cũ");
            _rollbackButton.Visible = false;
            _rollbackButton.Click += OnRollbackClicked;

            _refreshButton = UiTheme.SecondaryButton("Kiểm tra lại");
            _refreshButton.Click += async (s, e) => await RefreshAsync();

            _closeButton = UiTheme.SecondaryButton("Đóng");
            _closeButton.Click += (s, e) => Close();

            // Footer bar
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(22), LogicalToDeviceUnits(10),
                                      LogicalToDeviceUnits(22), LogicalToDeviceUnits(10))
            };

            var footerInner = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            footerInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footerInner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var leftButtons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = true,
                BackColor = Color.Transparent
            };
            _actionButton.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), LogicalToDeviceUnits(4));
            _fileUpdateButton.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), LogicalToDeviceUnits(4));
            _rollbackButton.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(4));

            leftButtons.Controls.Add(_actionButton);
            leftButtons.Controls.Add(_fileUpdateButton);
            leftButtons.Controls.Add(_rollbackButton);

            footerInner.Controls.Add(leftButtons, 0, 0);
            footerInner.Controls.Add(UiTheme.ButtonRow(_closeButton, _refreshButton), 1, 0);
            footer.Controls.Add(footerInner);

            var footerBorder = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            Controls.Add(root);
            Controls.Add(footerBorder);
            Controls.Add(footer);

            CancelButton = _closeButton;

            MinimumSize = new Size(LogicalToDeviceUnits(640), LogicalToDeviceUnits(500));
            Size = new Size(LogicalToDeviceUnits(720), LogicalToDeviceUnits(560));

            void ApplyWrap()
            {
                int available = root.ClientSize.Width - root.Padding.Horizontal;
                UiTheme.ApplyWrapWidth(root, available);
                SizeColumns();
            }

            Shown += async (s, e) =>
            {
                ApplyWrap();
                _releaseList.Select();
                await RefreshAsync();
            };
            root.ClientSizeChanged += (s, e) => ApplyWrap();
        }

        private void SizeColumns()
        {
            if (_releaseList.Width < 80)
            {
                return;
            }

            int inner = _releaseList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            if (inner < 80)
            {
                inner = _releaseList.ClientSize.Width;
            }

            _releaseList.Columns[0].Width = Math.Max(100, inner * 20 / 100);
            _releaseList.Columns[1].Width = Math.Max(95, inner * 20 / 100);
            _releaseList.Columns[2].Width = Math.Max(120, inner * 25 / 100);
            _releaseList.Columns[3].Width = Math.Max(130, inner - _releaseList.Columns[0].Width
                - _releaseList.Columns[1].Width - _releaseList.Columns[2].Width);
        }

        private async Task RefreshAsync()
        {
            if (_isBusy)
            {
                return;
            }

            SetBusy(true);
            _statusLabel.Text = "Đang kiểm tra danh sách phiên bản...";
            _statusHeaderLabel.Text = $"Đang kiểm tra (Hiện tại: v{UpdateManager.CurrentVersionText})...";
            _statusHeaderLabel.ForeColor = UiTheme.TextPrimary;

            string feed = UpdateManager.Feed;
            string displayFeed = feed.Length > 55 ? feed.Substring(0, 25) + "..." + feed.Substring(feed.Length - 25) : feed;
            _feedInfoLabel.Text = $"Nguồn: {displayFeed}";

            _result = await UpdateManager.CheckAsync();

            if (IsDisposed || Disposing)
            {
                return;
            }

            _releaseList.Items.Clear();

            if (_result == null || !_result.Success)
            {
                _statusHeaderLabel.Text = $"Phiên bản hiện tại: v{UpdateManager.CurrentVersionText}";
                _statusHeaderLabel.ForeColor = UiTheme.TextPrimary;
                _statusSubLabel.Text = "Không đọc được nguồn cập nhật. Bạn vẫn có thể dùng nút \"Cập nhật từ file .exe...\".";
                _statusSubLabel.ForeColor = UiTheme.Warning;
                _statusLabel.Text = _result?.Error ?? "Không đọc được danh sách phiên bản.";
                SetBusy(false);
                UpdateActionButton();
                return;
            }

            foreach (ReleaseInfo release in _result.Releases)
            {
                var item = new ListViewItem($"v{release.Version}") { Tag = release };
                item.SubItems.Add(release.ChannelLabel);
                item.SubItems.Add(string.IsNullOrWhiteSpace(release.Released) ? "—" : release.Released);
                item.SubItems.Add(DescribeState(release));

                if (release.Channel == ReleaseChannel.Recommended)
                {
                    item.ForeColor = UiTheme.Success;
                }
                else if (release.Channel == ReleaseChannel.Experimental)
                {
                    item.ForeColor = UiTheme.Warning;
                }

                if (release.IsCurrent)
                {
                    item.Font = new Font(_releaseList.Font, FontStyle.Bold);
                }

                _releaseList.Items.Add(item);
            }

            _warningLabel.Visible = !UpdateManager.IsRunningInstalledCopy;

            if (_result.HasNewer)
            {
                ReleaseInfo newest = _result.Releases[0];
                _statusHeaderLabel.Text = $"★ Có phiên bản mới: v{newest.Version} (Hiện tại: v{UpdateManager.CurrentVersionText})";
                _statusHeaderLabel.ForeColor = UiTheme.Accent;
                _statusSubLabel.Text = $"Bản phát hành ngày {newest.Released}. Nhấn \"Cập nhật lên v{newest.Version}\" để cài đặt.";
                _statusSubLabel.ForeColor = UiTheme.TextPrimary;
                _statusLabel.Text = $"Có phiên bản mới hơn (v{newest.Version}).";
            }
            else
            {
                _statusHeaderLabel.Text = $"✔ Bạn đang sử dụng phiên bản mới nhất (v{UpdateManager.CurrentVersionText})";
                _statusHeaderLabel.ForeColor = UiTheme.Success;
                _statusSubLabel.Text = "Hệ thống đã kiểm tra tại nguồn cập nhật và không có bản mới hơn.";
                _statusSubLabel.ForeColor = UiTheme.TextMuted;
                _statusLabel.Text = $"Đang dùng bản mới nhất (v{UpdateManager.CurrentVersionText}).";
            }

            SelectSensibleRow();
            SetBusy(false);
            UpdateActionButton();
        }

        private static string DescribeState(ReleaseInfo release)
        {
            if (release.IsCurrent)
            {
                return release.IsCached ? "Đang dùng · đã tải về máy" : "Đang dùng";
            }
            return release.IsCached ? "Đã tải về máy (sẵn sàng)" : "Chưa tải";
        }

        private void SelectSensibleRow()
        {
            if (_releaseList.Items.Count == 0)
            {
                return;
            }

            // Ưu tiên chọn bản mới hơn nếu có
            ListViewItem? target = _releaseList.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(i => i.Tag is ReleaseInfo r && r.ParsedVersion > UpdateManager.CurrentVersion);

            // Nếu không có bản mới hơn, chọn bản đang dùng
            target ??= _releaseList.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(i => i.Tag is ReleaseInfo r && r.IsCurrent);

            // Cuối cùng mới lấy dòng đầu
            target ??= _releaseList.Items[0];
            target.Selected = true;
            target.EnsureVisible();
        }

        private ReleaseInfo? SelectedRelease =>
            _releaseList.SelectedItems.Count > 0 ? _releaseList.SelectedItems[0].Tag as ReleaseInfo : null;

        private void UpdateActionButton()
        {
            ReleaseInfo? release = SelectedRelease;

            _notesLabel.Text = release == null || string.IsNullOrWhiteSpace(release.Notes)
                ? ""
                : $"Thay đổi ở v{release.Version}: {release.Notes}";

            // Cho phép nút quay về khi người dùng chọn thủ công một bản cũ hơn bản đang dùng
            bool isDowngrade = release != null && release.ParsedVersion < UpdateManager.CurrentVersion;
            _rollbackButton.Visible = isDowngrade;
            _rollbackButton.Enabled = !_isBusy && isDowngrade;
            if (isDowngrade && release != null)
            {
                _rollbackButton.Text = $"Quay về v{release.Version}";
            }

            if (release == null || _isBusy)
            {
                _actionButton.Enabled = false;
                _actionButton.Text = "Cài đặt";
                return;
            }

            if (release.IsCurrent)
            {
                _actionButton.Text = "Đang dùng bản này";
                _actionButton.Enabled = false;
                return;
            }

            if (release.ParsedVersion > UpdateManager.CurrentVersion)
            {
                _actionButton.Enabled = true;
                _actionButton.Text = $"Cập nhật lên v{release.Version}";
            }
            else
            {
                _actionButton.Enabled = true;
                _actionButton.Text = $"Cài lại v{release.Version}";
            }
        }

        private async void OnActionClicked(object? sender, EventArgs e)
        {
            ReleaseInfo? release = SelectedRelease;
            if (release != null)
            {
                await InstallAsync(release);
            }
        }

        private async void OnRollbackClicked(object? sender, EventArgs e)
        {
            ReleaseInfo? release = SelectedRelease;
            if (release != null)
            {
                await InstallAsync(release);
            }
        }

        private void OnFileUpdateClicked(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Chọn file bộ cài HapiCopi (.exe)",
                Filter = "Bộ cài HapiCopi (*.exe)|*.exe|Tất cả tệp (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            string setupPath = dlg.FileName;
            string fileName = Path.GetFileName(setupPath);

            string question = $"Cài đặt cập nhật từ file:\n{fileName}?\n\n" +
                              "HapiCopi sẽ thoát, chạy trình cài đặt và tự khởi động lại sau khi hoàn tất.";

            if (MessageBox.Show(question, "HapiCopi — Cập nhật từ file", MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1) != DialogResult.OK)
            {
                return;
            }

            SetBusy(true);
            _statusLabel.Text = $"Đang khởi động bộ cài {fileName}...";

            var (started, message, ver) = UpdateManager.InstallFromFile(setupPath);
            if (!started)
            {
                SetBusy(false);
                _statusLabel.Text = message;
                MessageBox.Show(message, "HapiCopi — Lỗi cài đặt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(
                message + "\n\nNhấn OK để thoát HapiCopi và bắt đầu cài.",
                "HapiCopi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Application.Exit();
        }

        private void OnChangeFeedClicked(object? sender, EventArgs e)
        {
            var menu = new ContextMenuStrip { Font = UiTheme.Body };

            var itemFolder = new ToolStripMenuItem("📁 Chọn thư mục cập nhật (Máy / Mạng LAN)...");
            itemFolder.Click += async (s, ev) =>
            {
                using var fbd = new FolderBrowserDialog
                {
                    Description = "Chọn thư mục chứa versions.json và bộ cài HapiCopi",
                    UseDescriptionForTitle = true,
                    SelectedPath = Directory.Exists(UpdateManager.Feed) ? UpdateManager.Feed : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };

                if (fbd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    UpdateManager.SetFeed(fbd.SelectedPath);
                    await RefreshAsync();
                }
            };

            var itemUrl = new ToolStripMenuItem("🌐 Nhập địa chỉ nguồn (URL hoặc đường dẫn)...");
            itemUrl.Click += async (s, ev) =>
            {
                string? input = PromptInput(
                    "Cấu hình nguồn cập nhật",
                    "Nhập đường dẫn thư mục hoặc URL chứa versions.json:",
                    UpdateManager.Feed,
                    this);

                if (input != null)
                {
                    UpdateManager.SetFeed(input);
                    await RefreshAsync();
                }
            };

            var itemReset = new ToolStripMenuItem("↺ Khôi phục nguồn mặc định");
            itemReset.Click += async (s, ev) =>
            {
                UpdateManager.SetFeed("");
                await RefreshAsync();
            };

            menu.Items.Add(itemFolder);
            menu.Items.Add(itemUrl);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemReset);

            menu.Show(_changeFeedButton, new Point(0, _changeFeedButton.Height));
        }

        private static string? PromptInput(string title, string prompt, string defaultValue, Form parent)
        {
            using var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                Font = UiTheme.Body,
                BackColor = UiTheme.Background,
                ClientSize = new Size(parent.LogicalToDeviceUnits(480), parent.LogicalToDeviceUnits(160))
            };

            var lbl = new Label
            {
                Text = prompt,
                Dock = DockStyle.Top,
                Height = parent.LogicalToDeviceUnits(36),
                Padding = new Padding(parent.LogicalToDeviceUnits(14), parent.LogicalToDeviceUnits(10), parent.LogicalToDeviceUnits(14), 0),
                Font = UiTheme.Body
            };

            var txt = new TextBox
            {
                Text = defaultValue,
                Dock = DockStyle.Top,
                Font = UiTheme.Body
            };

            var txtHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = parent.LogicalToDeviceUnits(36),
                Padding = new Padding(parent.LogicalToDeviceUnits(14), 0, parent.LogicalToDeviceUnits(14), 0)
            };
            txtHost.Controls.Add(txt);

            var btnOk = UiTheme.PrimaryButton("Đồng ý");
            var btnCancel = UiTheme.SecondaryButton("Hủy");
            btnOk.DialogResult = DialogResult.OK;
            btnCancel.DialogResult = DialogResult.Cancel;

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = parent.LogicalToDeviceUnits(48),
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(parent.LogicalToDeviceUnits(14), parent.LogicalToDeviceUnits(8), parent.LogicalToDeviceUnits(14), parent.LogicalToDeviceUnits(8))
            };
            btnRow.Controls.Add(btnCancel);
            btnRow.Controls.Add(btnOk);

            form.Controls.Add(btnRow);
            form.Controls.Add(txtHost);
            form.Controls.Add(lbl);
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;

            return form.ShowDialog(parent) == DialogResult.OK ? txt.Text.Trim() : null;
        }

        private async Task InstallAsync(ReleaseInfo release)
        {
            bool isDowngrade = release.ParsedVersion < UpdateManager.CurrentVersion;
            string question = isDowngrade
                ? $"Quay về phiên bản v{release.Version} ({release.ChannelLabel})?\n\n" +
                  "HapiCopi sẽ thoát, cài lại phiên bản này rồi tự khởi động lại."
                : $"Cài đặt cập nhật phiên bản v{release.Version} ({release.ChannelLabel})?\n\n" +
                  "HapiCopi sẽ thoát, cài bản mới rồi tự khởi động lại.";

            if (MessageBox.Show(question, "HapiCopi — Cập nhật", MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1) != DialogResult.OK)
            {
                return;
            }

            SetBusy(true);
            _progress.Visible = true;
            _progress.Value = 0;
            _statusLabel.Text = $"Đang chuẩn bị bộ cài v{release.Version}...";

            var progress = new Progress<int>(percent =>
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }
                _progress.Value = Math.Clamp(percent, 0, 100);
                _statusLabel.Text = percent >= 100
                    ? $"Đã có bộ cài v{release.Version}. Đang khởi động trình cài đặt..."
                    : $"Đang tải v{release.Version}... {percent}%";
            });

            (bool started, string message) = await UpdateManager.InstallAsync(release, progress, CancellationToken.None);

            if (IsDisposed || Disposing)
            {
                return;
            }

            if (!started)
            {
                _progress.Visible = false;
                _statusLabel.Text = message;
                SetBusy(false);
                UpdateActionButton();
                MessageBox.Show(message, "HapiCopi — Lỗi cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _statusLabel.Text = message;

            MessageBox.Show(
                message + "\n\nNhấn OK để thoát HapiCopi và bắt đầu cài.",
                "HapiCopi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Application.Exit();
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            _refreshButton.Enabled = !busy;
            _fileUpdateButton.Enabled = !busy;
            _changeFeedButton.Enabled = !busy;
            _releaseList.Enabled = !busy;

            if (busy)
            {
                _actionButton.Enabled = false;
                _rollbackButton.Enabled = false;
            }
        }
    }
}
