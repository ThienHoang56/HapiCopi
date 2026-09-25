using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Cửa sổ quản lý phiên bản: cập nhật lên bản mới hoặc quay về bản đã chạy ổn.
    /// </summary>
    public class UpdateForm : Form
    {
        private readonly Label _currentLabel;
        private readonly TextBox _feedBox;
        private readonly Label _warningLabel;
        private readonly ListView _releaseList;
        private readonly Label _notesLabel;
        private readonly ProgressBar _progress;
        private readonly Label _statusLabel;
        private readonly Button _actionButton;
        private readonly Button _rollbackButton;
        private readonly Button _refreshButton;

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
                Padding = new Padding(LogicalToDeviceUnits(22), LogicalToDeviceUnits(18),
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

            AddAuto(UiTheme.PageTitleLabel("Phiên bản và cập nhật"), 10);

            _currentLabel = new Label
            {
                Text = $"Đang dùng: v{UpdateManager.CurrentVersionText}",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                UseMnemonic = false
            };
            AddAuto(_currentLabel, 12);

            AddAuto(UiTheme.Description(
                "Nguồn cập nhật: thư mục trên máy, thư mục mạng LAN, hoặc URL https."), 6);

            var feedRow = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(8))
            };
            feedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            feedRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _feedBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.Small
            };
            feedRow.Controls.Add(_feedBox, 0, 0);

            Button saveFeedButton = UiTheme.SecondaryButton("Lưu nguồn");
            saveFeedButton.Margin = new Padding(LogicalToDeviceUnits(8), 0, 0, 0);
            saveFeedButton.Click += async (s, e) =>
            {
                AppConfig.Set("UPDATE_FEED", _feedBox.Text.Trim());
                await RefreshAsync();
            };
            feedRow.Controls.Add(saveFeedButton, 1, 0);
            AddAuto(feedRow, 8);

            _warningLabel = UiTheme.Hint("", UiTheme.Danger);
            _warningLabel.Visible = false;
            AddAuto(_warningLabel, 8);

            _releaseList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                GridLines = false,
                Font = UiTheme.Body
            };
            _releaseList.Columns.Add("Phiên bản", 110);
            _releaseList.Columns.Add("Kênh", 110);
            _releaseList.Columns.Add("Ngày phát hành", 130);
            _releaseList.Columns.Add("Trạng thái", 210);
            _releaseList.SelectedIndexChanged += (s, e) => UpdateActionButton();
            _releaseList.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(8));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(_releaseList, 0, row++);

            _notesLabel = UiTheme.Description("");
            AddAuto(_notesLabel, 6);

            _progress = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Height = LogicalToDeviceUnits(14),
                Visible = false
            };
            AddAuto(_progress, 6);

            _statusLabel = UiTheme.Description("Đang đọc danh sách phiên bản...");
            AddAuto(_statusLabel, 4);

            _actionButton = UiTheme.PrimaryButton("Cài bản này");
            _actionButton.Enabled = false;
            _actionButton.Click += OnActionClicked;

            _rollbackButton = UiTheme.SecondaryButton("Quay về bản khuyến nghị");
            _rollbackButton.Enabled = false;
            _rollbackButton.Click += OnRollbackClicked;

            _refreshButton = UiTheme.SecondaryButton("Kiểm tra lại");
            _refreshButton.Click += async (s, e) => await RefreshAsync();

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
            _rollbackButton.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(4));
            leftButtons.Controls.Add(_actionButton);
            leftButtons.Controls.Add(_rollbackButton);

            footerInner.Controls.Add(leftButtons, 0, 0);
            footerInner.Controls.Add(UiTheme.ButtonRow(closeButton, _refreshButton), 1, 0);
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

            CancelButton = closeButton;

            MinimumSize = new Size(LogicalToDeviceUnits(640), LogicalToDeviceUnits(480));
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

            _releaseList.Columns[0].Width = Math.Max(90, inner * 18 / 100);
            _releaseList.Columns[1].Width = Math.Max(90, inner * 20 / 100);
            _releaseList.Columns[2].Width = Math.Max(100, inner * 22 / 100);
            _releaseList.Columns[3].Width = Math.Max(120, inner - _releaseList.Columns[0].Width
                - _releaseList.Columns[1].Width - _releaseList.Columns[2].Width);
        }

        private async Task RefreshAsync()
        {
            if (_isBusy)
            {
                return;
            }

            SetBusy(true);
            _statusLabel.Text = "Đang đọc danh sách phiên bản...";
            _feedBox.Text = UpdateManager.Feed;

            _result = await UpdateManager.CheckAsync();

            if (IsDisposed || Disposing)
            {
                return;
            }

            _releaseList.Items.Clear();

            if (_result == null || !_result.Success)
            {
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
            _warningLabel.Text = _warningLabel.Visible
                ? "Bản đang chạy không phải bản cài đặt (chạy từ thư mục build). " +
                  "Cài đặt sẽ tạo bản cài riêng chứ không thay thế thư mục này."
                : "";

            _statusLabel.Text = _result.HasNewer
                ? $"Có bản mới hơn bản đang dùng (v{UpdateManager.CurrentVersionText})."
                : $"Đang dùng bản mới nhất có trong nguồn (v{UpdateManager.CurrentVersionText}).";

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
            return release.IsCached ? "Đã tải về máy (quay lui được khi offline)" : "Chưa tải";
        }

        private void SelectSensibleRow()
        {
            if (_releaseList.Items.Count == 0)
            {
                return;
            }

            // Chỉ tự chọn bản mới hơn khi thật sự có bản mới. Không có thì trỏ về bản đang dùng,
            // tránh mặc định mời người dùng quay lui.
            ListViewItem? target = _releaseList.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(i => i.Tag is ReleaseInfo r && r.ParsedVersion > UpdateManager.CurrentVersion);

            target ??= _releaseList.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(i => i.Tag is ReleaseInfo r && r.IsCurrent);

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

            _rollbackButton.Enabled = !_isBusy
                                      && _result?.Recommended != null
                                      && !_result.Recommended.IsCurrent;

            if (release == null || _isBusy)
            {
                _actionButton.Enabled = false;
                return;
            }

            if (release.IsCurrent)
            {
                _actionButton.Text = "Đang dùng bản này";
                _actionButton.Enabled = false;
                return;
            }

            _actionButton.Enabled = true;
            _actionButton.Text = release.ParsedVersion > UpdateManager.CurrentVersion
                ? $"Cập nhật lên v{release.Version}"
                : $"Quay về v{release.Version}";
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
            ReleaseInfo? recommended = _result?.Recommended;
            if (recommended != null)
            {
                await InstallAsync(recommended);
            }
        }

        private async Task InstallAsync(ReleaseInfo release)
        {
            bool isDowngrade = release.ParsedVersion < UpdateManager.CurrentVersion;
            string question = isDowngrade
                ? $"Quay về phiên bản {release.Version} ({release.ChannelLabel})?\n\n" +
                  "HapiCopi sẽ thoát, cài lại bản này rồi tự khởi động lại."
                : $"Cài phiên bản {release.Version} ({release.ChannelLabel})?\n\n" +
                  "HapiCopi sẽ thoát, cài bản mới rồi tự khởi động lại.";

            if (MessageBox.Show(question, "HapiCopi", MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
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
                MessageBox.Show(message, "HapiCopi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            _releaseList.Enabled = !busy;
            if (busy)
            {
                _actionButton.Enabled = false;
                _rollbackButton.Enabled = false;
            }
        }
    }
}
