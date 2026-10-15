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
    /// Cửa sổ Phiên bản & Cập nhật: Dạng Card Mini Popup tinh gọn, responsive, tự giải thích.
    /// </summary>
    public class UpdateForm : Form
    {
        private readonly Label _titleLabel;
        private readonly Label _statusBadgeLabel;
        private readonly Label _statusDetailLabel;
        private readonly Label _notesLabel;
        private readonly ProgressBar _progress;
        private readonly Label _progressLabel;
        private readonly LinkLabel _feedLink;
        private readonly Button _actionButton;
        private readonly Button _fileUpdateButton;
        private readonly Button _closeButton;

        private UpdateCheckResult? _result;
        private ReleaseInfo? _latestRelease;
        private bool _isBusy;

        public UpdateForm()
        {
            UiTheme.ApplyDialog(this, "HapiCopi — Phiên bản và cập nhật");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // Kích thước chuẩn Mini Popup gọn nhẹ, tinh tế
            MinimumSize = new Size(LogicalToDeviceUnits(440), LogicalToDeviceUnits(360));
            ClientSize = new Size(LogicalToDeviceUnits(480), LogicalToDeviceUnits(390));
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
            var title = UiTheme.PageTitleLabel("Phiên bản & Cập nhật");
            title.Dock = DockStyle.Fill;
            header.Controls.Add(title);

            var headerBorder = new Panel
            {
                Dock = DockStyle.Top,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            // Body Card Layout
            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Background,
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(12),
                                      LogicalToDeviceUnits(16), LogicalToDeviceUnits(8)),
                AutoScroll = true
            };

            var card = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(14),
                                      LogicalToDeviceUnits(16), LogicalToDeviceUnits(14))
            };
            card.Paint += (s, e) =>
            {
                using var pen = new Pen(UiTheme.Border, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            var cardLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            int row = 0;
            void AddCardRow(Control ctrl, int bottomGap = 6)
            {
                ctrl.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(bottomGap));
                cardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                cardLayout.Controls.Add(ctrl, 0, row++);
            }

            // 1. Tiêu đề phiên bản hiện tại
            _titleLabel = new Label
            {
                Text = $"HapiCopi v{UpdateManager.CurrentVersionText}",
                Font = UiTheme.PageTitle,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true
            };
            AddCardRow(_titleLabel, 6);

            // 2. Badge trạng thái
            _statusBadgeLabel = new Label
            {
                Text = "Đang kiểm tra máy chủ cập nhật...",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.Accent,
                AutoSize = true
            };
            AddCardRow(_statusBadgeLabel, 4);

            // 3. Chi tiết trạng thái
            _statusDetailLabel = new Label
            {
                Text = "Hệ thống đang kết nối để tìm kiếm bản cập nhật mới nhất...",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true
            };
            AddCardRow(_statusDetailLabel, 8);

            // 4. Release Notes tóm tắt
            _notesLabel = new Label
            {
                Text = "",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Visible = false
            };
            AddCardRow(_notesLabel, 8);

            // 5. Thanh tiến độ tải
            _progress = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Height = LogicalToDeviceUnits(6),
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };
            AddCardRow(_progress, 4);

            _progressLabel = new Label
            {
                Text = "",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Visible = false
            };
            AddCardRow(_progressLabel, 6);

            // 6. Link đổi nguồn
            _feedLink = new LinkLabel
            {
                Text = $"Nguồn: {UpdateManager.Feed} (Nhấp để đổi)",
                Font = UiTheme.Small,
                LinkColor = UiTheme.Accent,
                ActiveLinkColor = UiTheme.AccentHover,
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            _feedLink.LinkClicked += (s, e) => OnChangeFeedClicked(s, e);
            AddCardRow(_feedLink, 0);

            card.Controls.Add(cardLayout);
            body.Controls.Add(card);

            // Footer Bar
            var footerBorder = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = LogicalToDeviceUnits(58),
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(12),
                                      LogicalToDeviceUnits(16), LogicalToDeviceUnits(12))
            };

            var footerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = Padding.Empty
            };
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _fileUpdateButton = new Button
            {
                Text = "Cài từ file .exe...",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0)
            };
            _fileUpdateButton.FlatAppearance.BorderSize = 0;
            _fileUpdateButton.Click += OnFileUpdateClicked;

            var rightButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = Padding.Empty
            };

            _actionButton = UiTheme.PrimaryButton("Kiểm tra lại");
            _actionButton.Click += OnActionClicked;

            _closeButton = UiTheme.SecondaryButton("Đóng");
            _closeButton.Click += (s, e) => Close();
            _closeButton.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0);

            rightButtons.Controls.Add(_actionButton);
            rightButtons.Controls.Add(_closeButton);

            footerLayout.Controls.Add(_fileUpdateButton, 0, 0);
            footerLayout.Controls.Add(rightButtons, 1, 0);
            footer.Controls.Add(footerLayout);

            Controls.Add(body);
            Controls.Add(headerBorder);
            Controls.Add(header);
            Controls.Add(footerBorder);
            Controls.Add(footer);
            body.BringToFront();

            CancelButton = _closeButton;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            Shown += async (s, e) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (_isBusy) return;

            SetBusy(true);
            _statusBadgeLabel.Text = "Đang kiểm tra cập nhật...";
            _statusBadgeLabel.ForeColor = UiTheme.Accent;
            _statusDetailLabel.Text = "Hệ thống đang kết nối đến máy chủ...";
            _notesLabel.Visible = false;

            string feed = UpdateManager.Feed;
            string displayFeed = feed.Length > 40 ? feed.Substring(0, 18) + "…" + feed.Substring(feed.Length - 18) : feed;
            _feedLink.Text = $"Nguồn: {displayFeed} (Đổi nguồn)";

            _result = await UpdateManager.CheckAsync();

            if (IsDisposed || Disposing) return;
            SetBusy(false);

            if (_result == null || !_result.Success)
            {
                _statusBadgeLabel.Text = "Không thể kết nối máy chủ";
                _statusBadgeLabel.ForeColor = UiTheme.Warning;
                _statusDetailLabel.Text = _result?.Error ?? "Vui lòng kiểm tra lại mạng hoặc cập nhật từ file .exe.";
                _actionButton.Text = "Thử lại";
                _actionButton.Enabled = true;
                return;
            }

            // Tìm bản release mới nhất
            _latestRelease = _result.Releases.FirstOrDefault(r => r.ParsedVersion > UpdateManager.CurrentVersion);

            if (_latestRelease != null)
            {
                _statusBadgeLabel.Text = $"★ Đã có phiên bản mới: v{_latestRelease.Version}";
                _statusBadgeLabel.ForeColor = UiTheme.Warning;
                _statusDetailLabel.Text = $"Phát hành: {_latestRelease.Released} ({_latestRelease.ChannelLabel})";

                if (!string.IsNullOrWhiteSpace(_latestRelease.Notes))
                {
                    _notesLabel.Text = $"Thay đổi: {_latestRelease.Notes}";
                    _notesLabel.Visible = true;
                }

                _actionButton.Text = $"Cập nhật lên v{_latestRelease.Version}";
                _actionButton.Enabled = true;
            }
            else
            {
                _statusBadgeLabel.Text = "✓ Ứng dụng đang ở phiên bản mới nhất";
                _statusBadgeLabel.ForeColor = UiTheme.Success;
                _statusDetailLabel.Text = $"Bạn đang sử dụng phiên bản v{UpdateManager.CurrentVersionText} hoàn chỉnh nhất.";
                _actionButton.Text = "Kiểm tra lại";
                _actionButton.Enabled = true;
            }
        }

        private async void OnActionClicked(object? sender, EventArgs e)
        {
            if (_latestRelease != null)
            {
                await InstallAsync(_latestRelease);
            }
            else
            {
                await RefreshAsync();
            }
        }

        private async Task InstallAsync(ReleaseInfo release)
        {
            if (_isBusy) return;

            string question = $"Bạn có muốn cập nhật HapiCopi lên v{release.Version}?\n\n" +
                              "Ứng dụng sẽ tự động tải bộ cài, đóng phiên hiện tại và khởi động lại sau khi hoàn tất.";

            if (MessageBox.Show(question, "Xác nhận cập nhật", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            SetBusy(true);
            _progress.Visible = true;
            _progressLabel.Visible = true;
            _progress.Value = 0;
            _statusBadgeLabel.Text = $"Đang tải bản cập nhật v{release.Version}...";
            _statusBadgeLabel.ForeColor = UiTheme.Accent;

            var progressReporter = new Progress<int>(pct =>
            {
                if (IsDisposed || Disposing) return;
                _progress.Value = Math.Clamp(pct, 0, 100);
                _progressLabel.Text = $"Tiến trình tải: {pct}%";
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var (started, message) = await UpdateManager.InstallAsync(release, progressReporter, cts.Token);

            if (IsDisposed || Disposing) return;
            SetBusy(false);
            _progress.Visible = false;
            _progressLabel.Visible = false;

            if (!started)
            {
                _statusBadgeLabel.Text = "Cập nhật không thành công";
                _statusBadgeLabel.ForeColor = UiTheme.Danger;
                _statusDetailLabel.Text = message;
                MessageBox.Show(this, "Không thể hoàn tất cập nhật:\n" + message, "Lỗi cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            string setupPath = dlg.FileName;
            string fileName = Path.GetFileName(setupPath);

            string question = $"Cài đặt cập nhật từ file:\n{fileName}?\n\n" +
                              "HapiCopi sẽ thoát, chạy trình cài đặt và tự khởi động lại sau khi hoàn tất.";

            if (MessageBox.Show(question, "Xác nhận cài đặt", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            SetBusy(true);
            var (started, message, version) = UpdateManager.InstallFromFile(setupPath);
            SetBusy(false);

            if (!started)
            {
                MessageBox.Show(this, "Không thể khởi chạy bộ cài:\n" + message, "Lỗi cài đặt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void OnChangeFeedClicked(object? sender, EventArgs e)
        {
            using var dlg = new Form
            {
                Text = "Đổi nguồn cập nhật",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(LogicalToDeviceUnits(440), LogicalToDeviceUnits(160)),
                BackColor = UiTheme.Surface
            };

            var lbl = new Label
            {
                Text = "Nhập URL Web (HTTPS), thư mục mạng (UNC) hoặc đường dẫn thư mục cục bộ:",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                Dock = DockStyle.Top,
                Padding = new Padding(LogicalToDeviceUnits(14), LogicalToDeviceUnits(10), LogicalToDeviceUnits(14), 0),
                AutoSize = true
            };

            var txt = new TextBox
            {
                Text = UpdateManager.Feed,
                Font = UiTheme.Body,
                Dock = DockStyle.Top,
                Margin = new Padding(LogicalToDeviceUnits(14), LogicalToDeviceUnits(6), LogicalToDeviceUnits(14), 0)
            };

            var btnPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = LogicalToDeviceUnits(54),
                Padding = new Padding(LogicalToDeviceUnits(14), LogicalToDeviceUnits(10), LogicalToDeviceUnits(14), LogicalToDeviceUnits(10))
            };

            var btnOk = UiTheme.PrimaryButton("Lưu");
            btnOk.Click += (s2, e2) => { dlg.DialogResult = DialogResult.OK; dlg.Close(); };

            var btnCancel = UiTheme.SecondaryButton("Hủy");
            btnCancel.Click += (s2, e2) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

            btnPanel.Controls.Add(UiTheme.ButtonRow(btnCancel, btnOk));

            dlg.Controls.Add(btnPanel);
            dlg.Controls.Add(txt);
            dlg.Controls.Add(lbl);

            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(txt.Text))
            {
                UpdateManager.SetFeed(txt.Text.Trim());
                await RefreshAsync();
            }
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            _actionButton.Enabled = !busy;
            _fileUpdateButton.Enabled = !busy;
            _feedLink.Enabled = !busy;
        }
    }
}
