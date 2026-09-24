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
        private readonly Label _feedLabel;
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

        private static readonly Color ColorRecommended = Color.FromArgb(39, 174, 96);
        private static readonly Color ColorExperimental = Color.FromArgb(230, 126, 34);

        public UpdateForm()
        {
            Text = "HapiCopi - Phiên bản & Cập nhật";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 580);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            int margin = 20;
            int width = ClientSize.Width - (margin * 2);
            int y = 18;

            var header = new Label
            {
                Text = "🔄 Phiên bản & Cập nhật",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 152, 219),
                AutoSize = true,
                Location = new Point(margin, y)
            };
            Controls.Add(header);
            y += 34;

            _currentLabel = new Label
            {
                Text = $"Đang dùng: v{UpdateManager.CurrentVersionText}",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(margin, y)
            };
            Controls.Add(_currentLabel);
            y += 24;

            _feedLabel = new Label
            {
                Text = "Nguồn cập nhật (thư mục trên máy, thư mục mạng LAN, hoặc URL https):",
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(margin, y),
                Size = new Size(width, 16)
            };
            Controls.Add(_feedLabel);
            y += 18;

            _feedBox = new TextBox
            {
                Location = new Point(margin, y),
                Width = width - 96,
                Font = new Font("Segoe UI", 8.5f)
            };
            Controls.Add(_feedBox);

            var saveFeedButton = new Button
            {
                Text = "Lưu nguồn",
                Width = 90,
                Height = 24,
                Location = new Point(margin + width - 90, y - 1),
                FlatStyle = FlatStyle.Flat
            };
            saveFeedButton.Click += async (s, e) =>
            {
                AppConfig.Set("UPDATE_FEED", _feedBox.Text.Trim());
                await RefreshAsync();
            };
            Controls.Add(saveFeedButton);
            y += 30;

            _warningLabel = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(192, 57, 43),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(margin, y),
                Size = new Size(width, 30),
                Visible = false
            };
            Controls.Add(_warningLabel);
            y += 34;

            _releaseList = new ListView
            {
                Location = new Point(margin, y),
                Size = new Size(width, 210),
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                GridLines = false
            };
            _releaseList.Columns.Add("Phiên bản", 110);
            _releaseList.Columns.Add("Kênh", 110);
            _releaseList.Columns.Add("Ngày phát hành", 130);
            _releaseList.Columns.Add("Trạng thái", 210);
            _releaseList.SelectedIndexChanged += (s, e) => UpdateActionButton();
            Controls.Add(_releaseList);
            y += 220;

            _notesLabel = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(90, 90, 90),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(margin, y),
                Size = new Size(width, 46)
            };
            Controls.Add(_notesLabel);
            y += 52;

            _progress = new ProgressBar
            {
                Location = new Point(margin, y),
                Size = new Size(width, 14),
                Visible = false
            };
            Controls.Add(_progress);
            y += 20;

            _statusLabel = new Label
            {
                Text = "Đang đọc danh sách phiên bản...",
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(margin, y),
                Size = new Size(width, 34)
            };
            Controls.Add(_statusLabel);

            int buttonY = ClientSize.Height - 46;

            _actionButton = new Button
            {
                Text = "Cài bản này",
                Width = 170,
                Height = 32,
                Location = new Point(margin, buttonY),
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            _actionButton.FlatAppearance.BorderSize = 0;
            _actionButton.Click += OnActionClicked;
            Controls.Add(_actionButton);

            _rollbackButton = new Button
            {
                Text = "Quay về bản khuyến nghị",
                Width = 180,
                Height = 32,
                Location = new Point(margin + 180, buttonY),
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            _rollbackButton.Click += OnRollbackClicked;
            Controls.Add(_rollbackButton);

            _refreshButton = new Button
            {
                Text = "Kiểm tra lại",
                Width = 100,
                Height = 32,
                Location = new Point(ClientSize.Width - margin - 210, buttonY),
                FlatStyle = FlatStyle.Flat
            };
            _refreshButton.Click += async (s, e) => await RefreshAsync();
            Controls.Add(_refreshButton);

            var closeButton = new Button
            {
                Text = "Đóng",
                Width = 100,
                Height = 32,
                Location = new Point(ClientSize.Width - margin - 100, buttonY),
                FlatStyle = FlatStyle.Flat
            };
            closeButton.Click += (s, e) => Close();
            Controls.Add(closeButton);

            CancelButton = closeButton;

            Shown += async (s, e) =>
            {
                // Nút cài là control đầu tiên trong Controls nên mặc định được focus; đẩy focus sang
                // danh sách để một phím Enter lạc không khởi động luôn việc đổi phiên bản.
                _releaseList.Select();
                await RefreshAsync();
            };
        }

        // ============================
        // NẠP DANH SÁCH
        // ============================

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
                    item.ForeColor = ColorRecommended;
                }
                else if (release.Channel == ReleaseChannel.Experimental)
                {
                    item.ForeColor = ColorExperimental;
                }

                if (release.IsCurrent)
                {
                    item.Font = new Font(_releaseList.Font, FontStyle.Bold);
                }

                _releaseList.Items.Add(item);
            }

            _warningLabel.Visible = !UpdateManager.IsRunningInstalledCopy;
            if (_warningLabel.Visible)
            {
                _warningLabel.Text = "⚠ Bản đang chạy không phải bản cài đặt (chạy từ thư mục build). " +
                                     "Cài đặt sẽ tạo bản cài riêng chứ không thay thế thư mục này.";
            }

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

            // Ưu tiên gợi ý bản mới hơn bản đang chạy; nếu không có thì trỏ về bản khuyến nghị.
            ListViewItem? target = _releaseList.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(i => i.Tag is ReleaseInfo r && r.ParsedVersion > UpdateManager.CurrentVersion);

            target ??= _releaseList.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(i => i.Tag is ReleaseInfo r && r.Channel == ReleaseChannel.Recommended);

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

        // ============================
        // CÀI ĐẶT
        // ============================

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

            // Mặc định chọn Hủy: đổi phiên bản là việc nặng, không nên chỉ cách một phím Enter.
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

            // Bộ cài đang chờ tiến trình này thoát mới ghi đè được file thực thi.
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
