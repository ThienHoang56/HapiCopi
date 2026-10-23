using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Giao diện Quản lý Đồng bộ Định dạng Tài liệu (StyleSync Dashboard).
    /// Cung cấp 2 chế độ rõ ràng, độc lập:
    /// 1. Chế độ Thủ công (Manual): Kéo thả hoặc chọn file .docx từ máy tính.
    /// 2. Chế độ Tự động (Auto): Tự động phát hiện Google Docs từ trình duyệt, tải ngầm và bóc tách cấu trúc.
    /// </summary>
    public class StyleSyncForm : Form
    {
        private readonly ComboBox _profileSelector;
        private readonly Label _activeDocLabel;
        private readonly Label _bodyStyleDetailsLabel;
        private readonly Label _headingDetailsLabel;
        private readonly Label _listDetailsLabel;
        private readonly TextBox _docsUrlBox;
        private readonly Label _autoStatusLabel;
        private readonly Button _autoFetchButton;
        private readonly Button _toggleActiveButton;
        private readonly Button _deleteProfileButton;
        private readonly CheckBox _renumberOutlineCheck;
        private readonly ComboBox _targetModeCombo;
        private readonly Panel _dropZone;
        private bool _suppressRenumberEvent;
        private bool _suppressTargetEvent;

        public StyleSyncForm()
        {
            UiTheme.ApplyDialog(this, "HapiCopi — Đồng bộ Định dạng Tài liệu (StyleSync)");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(LogicalToDeviceUnits(580), LogicalToDeviceUnits(680));
            KeyPreview = true;

            // ============================
            // HEADER BAR
            // ============================
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = LogicalToDeviceUnits(50),
                Padding = new Padding(LogicalToDeviceUnits(20), LogicalToDeviceUnits(12), LogicalToDeviceUnits(20), 0),
                BackColor = UiTheme.Surface
            };
            var title = UiTheme.PageTitleLabel("Đồng bộ Định dạng Tài liệu (StyleSync)");
            title.Dock = DockStyle.Fill;
            header.Controls.Add(title);

            var headerBorder = new Panel
            {
                Dock = DockStyle.Top,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            // ============================
            // BODY SCROLL CONTAINER
            // ============================
            var scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.Background,
                Padding = new Padding(LogicalToDeviceUnits(20), LogicalToDeviceUnits(14), LogicalToDeviceUnits(20), LogicalToDeviceUnits(14))
            };

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            int row = 0;
            void AddRow(Control ctrl, int bottomGap = 10)
            {
                ctrl.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(bottomGap));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                mainLayout.Controls.Add(ctrl, 0, row++);
            }

            // ============================
            // PHẦN 0: CHẾ ĐỘ ĐÍCH
            // ============================
            AddRow(UiTheme.SectionHeader("0. Chế độ đích khi Copy", UiTheme.Accent), 4);

            var targetPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                BackColor = Color.Transparent
            };
            targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78F));

            var targetLabel = new Label
            {
                Text = "Đích:",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 0)
            };

            _targetModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = UiTheme.Body,
                Dock = DockStyle.Fill
            };
            _targetModeCombo.Items.AddRange(new object[]
            {
                "Tự động (theo phím tắt Docs)",
                "Microsoft Word (.docx)",
                "Google Docs (giữ style gốc)"
            });
            _targetModeCombo.SelectedIndexChanged += OnTargetModeChanged;

            targetPanel.Controls.Add(targetLabel, 0, 0);
            targetPanel.Controls.Add(_targetModeCombo, 1, 0);
            AddRow(targetPanel, 6);

            var targetDesc = new Label
            {
                Text = "Google Docs đã tự áp font/size theo tài liệu đích. Ở chế độ Docs, HapiCopi chỉ đồng bộ cấu trúc: heading, bullet/numbering, đánh số mục (4.5 → 4.4).",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                MaximumSize = new Size(LogicalToDeviceUnits(530), 0)
            };
            AddRow(targetDesc, 10);
            AddRow(UiTheme.Separator(this), 12);

            // ============================
            // PHẦN 1: HỒ SƠ TÀI LIỆU HIỆN HÀNH
            // ============================
            AddRow(UiTheme.SectionHeader("1. Hồ sơ Tài liệu Đang Kích hoạt", UiTheme.Accent), 4);

            var profileSelectorPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                BackColor = Color.Transparent
            };
            profileSelectorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            profileSelectorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

            _profileSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = UiTheme.Body,
                Dock = DockStyle.Fill,
                Height = LogicalToDeviceUnits(30)
            };
            _profileSelector.SelectedIndexChanged += OnProfileSelectedChanged;

            _toggleActiveButton = UiTheme.SecondaryButton("Kích hoạt");
            _toggleActiveButton.Dock = DockStyle.Fill;
            _toggleActiveButton.Click += OnToggleActiveClicked;

            profileSelectorPanel.Controls.Add(_profileSelector, 0, 0);
            profileSelectorPanel.Controls.Add(_toggleActiveButton, 1, 0);
            AddRow(profileSelectorPanel, 8);

            // Card chi tiết hồ sơ
            var profileCard = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(14)),
                BorderStyle = BorderStyle.FixedSingle
            };

            var cardLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _activeDocLabel = new Label
            {
                Text = "Tài liệu: Chưa nạp hồ sơ",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            _bodyStyleDetailsLabel = new Label
            {
                Text = "• Thân bài: Mặc định hệ thống",
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            _headingDetailsLabel = new Label
            {
                Text = "• Tiêu đề (Headings): Chưa có thông số",
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            _listDetailsLabel = new Label
            {
                Text = "• Danh sách (Lists): Mặc định",
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };

            _renumberOutlineCheck = new CheckBox
            {
                Text = "Tự động đánh số lại đề mục theo sườn tài liệu (4.5 → 4.4)",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 2, 0, 6)
            };
            _renumberOutlineCheck.CheckedChanged += OnRenumberOutlineChanged;

            _deleteProfileButton = new Button
            {
                Text = "Xóa hồ sơ này",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Danger,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            _deleteProfileButton.FlatAppearance.BorderSize = 0;
            _deleteProfileButton.Click += OnDeleteProfileClicked;

            cardLayout.Controls.Add(_activeDocLabel, 0, 0);
            cardLayout.Controls.Add(_bodyStyleDetailsLabel, 0, 1);
            cardLayout.Controls.Add(_headingDetailsLabel, 0, 2);
            cardLayout.Controls.Add(_listDetailsLabel, 0, 3);
            cardLayout.Controls.Add(_renumberOutlineCheck, 0, 4);
            cardLayout.Controls.Add(_deleteProfileButton, 0, 5);
            profileCard.Controls.Add(cardLayout);
            AddRow(profileCard, 14);

            AddRow(UiTheme.Separator(this), 12);

            // ============================
            // PHẦN 2: CHẾ ĐỘ MANUAL (NẠP FILE .DOCX)
            // ============================
            AddRow(UiTheme.SectionHeader("2. Chế độ Thủ công: Nạp tài liệu mẫu (.docx)", UiTheme.Accent), 4);

            var manualDesc = new Label
            {
                Text = "Chọn file Word hoặc kéo thả trực tiếp file .docx vào khung bên dưới. Bộ đọc OpenXML sẽ bóc tách chính xác Font, Size, Căn lề, Spacing, sườn đề mục (4.3 → 4.4) và khuôn bullet/numbering mà không đoán mò.",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                MaximumSize = new Size(LogicalToDeviceUnits(530), 0)
            };
            AddRow(manualDesc, 8);

            // Khung Kéo Thả (Drop Zone)
            _dropZone = new Panel
            {
                Dock = DockStyle.Top,
                Height = LogicalToDeviceUnits(90),
                BackColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.FixedSingle,
                AllowDrop = true,
                Cursor = Cursors.Hand
            };
            var dropZoneLabel = new Label
            {
                Text = "📄 Kéo thả file .docx vào đây\nhoặc click để mở trình chọn file",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.Accent,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand
            };
            _dropZone.Controls.Add(dropZoneLabel);

            // Sự kiện kéo thả
            _dropZone.DragEnter += OnDropZoneDragEnter;
            _dropZone.DragDrop += OnDropZoneDragDrop;
            dropZoneLabel.DragEnter += OnDropZoneDragEnter;
            dropZoneLabel.DragDrop += OnDropZoneDragDrop;

            // Click mở OpenFileDialog
            _dropZone.Click += (s, e) => SelectManualDocxFile();
            dropZoneLabel.Click += (s, e) => SelectManualDocxFile();

            AddRow(_dropZone, 14);

            AddRow(UiTheme.Separator(this), 12);

            // ============================
            // PHẦN 3: CHẾ ĐỘ AUTO (GOOGLE DOCS)
            // ============================
            AddRow(UiTheme.SectionHeader("3. Chế độ Tự động: Quét & Phân tích từ Google Docs", UiTheme.Accent), 4);

            var autoDesc = new Label
            {
                Text = "Tự động phát hiện Google Docs đang mở trên trình duyệt (Chrome, Edge) hoặc dán link tài liệu. Hệ thống sẽ xuất ngầm .docx và bóc tách cấu trúc OpenXML.",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                MaximumSize = new Size(LogicalToDeviceUnits(530), 0)
            };
            AddRow(autoDesc, 8);

            _docsUrlBox = new TextBox
            {
                Font = UiTheme.Body,
                Dock = DockStyle.Top,
                PlaceholderText = "https://docs.google.com/document/d/... (Để trống nếu đang mở trên Chrome/Edge)"
            };
            AddRow(_docsUrlBox, 8);

            var autoButtonRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                BackColor = Color.Transparent
            };
            autoButtonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            autoButtonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            _autoFetchButton = UiTheme.PrimaryButton("⚡ Quét & Tự động Phân tích");
            _autoFetchButton.Dock = DockStyle.Fill;
            _autoFetchButton.Click += OnAutoFetchDocsClicked;

            Button detectUrlBtn = UiTheme.SecondaryButton("Lấy URL từ Trình duyệt");
            detectUrlBtn.Dock = DockStyle.Fill;
            detectUrlBtn.Click += OnDetectBrowserUrlClicked;

            autoButtonRow.Controls.Add(detectUrlBtn, 0, 0);
            autoButtonRow.Controls.Add(_autoFetchButton, 1, 0);
            AddRow(autoButtonRow, 6);

            _autoStatusLabel = new Label
            {
                Text = "Sẵn sàng quét tài liệu Google Docs.",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true
            };
            AddRow(_autoStatusLabel, 10);

            scrollPanel.Controls.Add(mainLayout);

            // ============================
            // FOOTER BAR
            // ============================
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = LogicalToDeviceUnits(54),
                BackColor = UiTheme.Surface,
                Padding = new Padding(LogicalToDeviceUnits(20), LogicalToDeviceUnits(10), LogicalToDeviceUnits(20), LogicalToDeviceUnits(10))
            };

            var closeBtn = UiTheme.SecondaryButton("Đóng");
            closeBtn.Click += (s, e) => Close();

            footer.Controls.Add(UiTheme.ButtonRow(closeBtn));

            var footerBorder = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Math.Max(1, LogicalToDeviceUnits(1)),
                BackColor = UiTheme.Border
            };

            Controls.Add(scrollPanel);
            Controls.Add(headerBorder);
            Controls.Add(header);
            Controls.Add(footerBorder);
            Controls.Add(footer);
            scrollPanel.BringToFront();

            // Khởi tạo dữ liệu
            RefreshProfileList();
            UpdateActiveProfileDisplay();
            UpdateTargetModeDisplay();

            // Lắng nghe thay đổi hồ sơ ngoài luồng
            StyleSyncService.ActiveProfileChanged += _ => SafeInvoke(UpdateActiveProfileDisplay);
            StyleSyncService.StateChanged += _ => SafeInvoke(UpdateActiveProfileDisplay);
            StyleSyncService.TargetModeChanged += () => SafeInvoke(UpdateTargetModeDisplay);
        }

        private void SafeInvoke(Action act)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { Invoke(act); } catch { }
            }
            else
            {
                act();
            }
        }

        private void RefreshProfileList()
        {
            _profileSelector.Items.Clear();
            _profileSelector.Items.Add("-- Không dùng hồ sơ (Dán mặc định) --");

            foreach (var p in StyleSyncService.SavedProfiles)
            {
                _profileSelector.Items.Add(p.DocumentTitle ?? "Tài liệu không tên");
            }

            if (StyleSyncService.ActiveProfile != null && StyleSyncService.IsEnabled)
            {
                int idx = _profileSelector.Items.IndexOf(StyleSyncService.ActiveProfile.DocumentTitle ?? "");
                _profileSelector.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else
            {
                _profileSelector.SelectedIndex = 0;
            }
        }

        private void UpdateTargetModeDisplay()
        {
            _suppressTargetEvent = true;
            _targetModeCombo.SelectedIndex = StyleSyncService.TargetMode switch
            {
                "word" => 1,
                "docs" => 2,
                _ => 0
            };
            _suppressTargetEvent = false;
        }

        private void OnTargetModeChanged(object? sender, EventArgs e)
        {
            if (_suppressTargetEvent) return;

            string mode = _targetModeCombo.SelectedIndex switch
            {
                1 => "word",
                2 => "docs",
                _ => "auto"
            };

            StyleSyncService.SetTargetMode(mode);
        }

        private void UpdateActiveProfileDisplay()
        {
            var p = StyleSyncService.ActiveProfile;
            bool isSyncEnabled = StyleSyncService.IsEnabled && p != null;

            if (p != null)
            {
                _activeDocLabel.Text = $"Tài liệu: {p.DocumentTitle}" + (isSyncEnabled ? " [ĐANG KÍCH HOẠT]" : " [TẠM TẮT]");
                _activeDocLabel.ForeColor = isSyncEnabled ? UiTheme.Success : UiTheme.TextMuted;

                string alignStr = p.BodyStyle.Alignment switch
                {
                    1 => "Căn giữa (Center)",
                    2 => "Căn phải (Right)",
                    3 => "Đều 2 bên (Justify)",
                    _ => "Căn trái (Left)"
                };

                _bodyStyleDetailsLabel.Text = $"• Thân bài: Font '{p.BodyStyle.FontName}', Cỡ {p.BodyStyle.FontSize:0.#}pt, Giãn dòng {p.BodyStyle.LineSpacing:0.##}, {alignStr}";

                string hSummary = "";
                foreach (var kvp in p.Headings)
                {
                    if (kvp.Key > 3 && !p.Outline.Any(o => o.Level == kvp.Key)) continue;
                    string numberFormat = p.HeadingNumberFormats.TryGetValue(kvp.Key, out var fmt) ? $" [{fmt}]" : "";
                    hSummary += $"H{kvp.Key}: {kvp.Value.FontSize:0.#}pt {(kvp.Value.Bold ? "Đậm" : "")}{numberFormat} | ";
                }
                string outlineSummary = p.Outline.Count > 0
                    ? $" — Sườn: {p.Outline.Count} đề mục"
                    : " — ⚠ Hồ sơ cũ: hãy nạp lại file .docx để đồng bộ cấu trúc";
                _headingDetailsLabel.Text = string.IsNullOrEmpty(hSummary)
                    ? "• Tiêu đề (Headings): Chưa có Heading riêng" + outlineSummary
                    : $"• Tiêu đề: {hSummary.TrimEnd(' ', '|')}{outlineSummary}";

                int bulletLevels = p.BulletLists.Count > 0 ? p.BulletLists.Count : (p.Lists.Count > 0 ? p.Lists.Count : 0);
                int orderedLevels = p.OrderedLists.Count;
                string bulletMarker = p.BulletLists.TryGetValue(1, out var bl) ? bl.BulletOrPrefix
                    : (p.Lists.TryGetValue(1, out var l1) ? l1.BulletOrPrefix : "-");
                _listDetailsLabel.Text = $"• Danh sách: {bulletLevels} cấp bullet ('{bulletMarker}'), {orderedLevels} cấp số thứ tự";

                _suppressRenumberEvent = true;
                _renumberOutlineCheck.Checked = p.RenumberOutline;
                _renumberOutlineCheck.Enabled = true;
                _suppressRenumberEvent = false;

                _toggleActiveButton.Text = isSyncEnabled ? "Tạm tắt" : "Kích hoạt";
                _deleteProfileButton.Visible = true;
            }
            else
            {
                _activeDocLabel.Text = "Tài liệu: Chưa nạp hồ sơ nào";
                _activeDocLabel.ForeColor = UiTheme.TextMuted;
                _bodyStyleDetailsLabel.Text = "• Thân bài: Mặc định hệ thống";
                _headingDetailsLabel.Text = "• Tiêu đề: Mặc định hệ thống";
                _listDetailsLabel.Text = "• Danh sách: Mặc định hệ thống";

                _suppressRenumberEvent = true;
                _renumberOutlineCheck.Checked = false;
                _renumberOutlineCheck.Enabled = false;
                _suppressRenumberEvent = false;

                _toggleActiveButton.Text = "Kích hoạt";
                _deleteProfileButton.Visible = false;
            }
        }

        private void OnProfileSelectedChanged(object? sender, EventArgs e)
        {
            if (_profileSelector.SelectedIndex <= 0)
            {
                StyleSyncService.DeactivateProfile();
            }
            else
            {
                string? selectedTitle = _profileSelector.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(selectedTitle))
                {
                    StyleSyncService.SwitchProfile(selectedTitle);
                }
            }
            UpdateActiveProfileDisplay();
        }

        private void OnToggleActiveClicked(object? sender, EventArgs e)
        {
            if (StyleSyncService.ActiveProfile == null && _profileSelector.SelectedIndex > 0)
            {
                string? selectedTitle = _profileSelector.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(selectedTitle))
                {
                    StyleSyncService.SwitchProfile(selectedTitle);
                }
            }
            else
            {
                StyleSyncService.ToggleEnabled();
            }
            UpdateActiveProfileDisplay();
        }

        private void OnRenumberOutlineChanged(object? sender, EventArgs e)
        {
            if (_suppressRenumberEvent) return;

            var profile = StyleSyncService.ActiveProfile;
            if (profile == null) return;

            profile.RenumberOutline = _renumberOutlineCheck.Checked;
            StyleSyncService.SaveProfile(profile);

            Program.Log($"[StyleSync] Đánh số lại đề mục: {(_renumberOutlineCheck.Checked ? "BẬT" : "TẮT")} cho hồ sơ \"{profile.DocumentTitle}\".");
        }

        private void OnDeleteProfileClicked(object? sender, EventArgs e)
        {
            if (StyleSyncService.ActiveProfile != null)
            {
                string title = StyleSyncService.ActiveProfile.DocumentTitle ?? "";
                var confirm = MessageBox.Show($"Bạn có chắc muốn xóa hồ sơ \"{title}\" khỏi hệ thống?",
                    "Xác nhận xóa hồ sơ", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (confirm == DialogResult.Yes)
                {
                    StyleSyncService.DeleteProfile(title);
                    RefreshProfileList();
                    UpdateActiveProfileDisplay();
                }
            }
        }

        private void SelectManualDocxFile()
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Chọn tài liệu Word mẫu (.docx)",
                Filter = "Tài liệu Word (*.docx)|*.docx|Tất cả tệp (*.*)|*.*",
                CheckFileExists = true
            };

            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                LoadDocxFile(ofd.FileName);
            }
        }

        private void LoadDocxFile(string path)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                var tree = StyleSyncService.LoadManualProfile(path);
                RefreshProfileList();
                UpdateActiveProfileDisplay();

                MessageBox.Show(
                    $"Đã bóc tách cấu trúc thành công từ tài liệu:\n\"{tree.DocumentTitle}\"\n\n" +
                    $"• Phông chữ thân bài: {tree.BodyStyle.FontName} {tree.BodyStyle.FontSize:0.#}pt\n" +
                    $"• Giãn dòng: {tree.BodyStyle.LineSpacing:0.##}\n" +
                    $"• Headings: {tree.Headings.Count} cấp tiêu đề\n" +
                    $"• Sườn đề mục: {tree.Outline.Count} mục\n" +
                    $"• Danh sách: {tree.BulletLists.Count} cấp bullet / {tree.OrderedLists.Count} cấp số\n\n" +
                    $"Hồ sơ đã được lưu và kích hoạt để đồng hóa dữ liệu khi dán!",
                    "Nạp tài liệu thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi phân tích file .docx:\n{ex.Message}", "Lỗi phân tích", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OnDropZoneDragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                if (files.Length > 0 && files[0].EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effect = DragDropEffects.Copy;
                    _dropZone.BackColor = Color.FromArgb(224, 242, 254);
                    return;
                }
            }
            e.Effect = DragDropEffects.None;
        }

        private void OnDropZoneDragDrop(object? sender, DragEventArgs e)
        {
            _dropZone.BackColor = Color.FromArgb(241, 245, 249);
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                if (files.Length > 0 && files[0].EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    LoadDocxFile(files[0]);
                }
            }
        }

        private void OnDetectBrowserUrlClicked(object? sender, EventArgs e)
        {
            var activeApp = ActiveWindowHelper.GetActiveAppInfo();
            if (activeApp.IsGoogleDocs && !string.IsNullOrEmpty(activeApp.BrowserUrl))
            {
                _docsUrlBox.Text = activeApp.BrowserUrl;
                _autoStatusLabel.Text = $"Đã phát hiện Docs: {activeApp.WindowTitle}";
            }
            else
            {
                _autoStatusLabel.Text = "Chưa phát hiện cửa sổ Google Docs. Hãy mở Docs trên Chrome/Edge hoặc dán link.";
            }
        }

        private async void OnAutoFetchDocsClicked(object? sender, EventArgs e)
        {
            string url = _docsUrlBox.Text.Trim();
            _autoFetchButton.Enabled = false;
            Cursor = Cursors.WaitCursor;

            try
            {
                var tree = await StyleSyncService.AutoSyncGoogleDocsAsync(
                    string.IsNullOrEmpty(url) ? null : url,
                    status => SafeInvoke(() => _autoStatusLabel.Text = status));

                RefreshProfileList();
                UpdateActiveProfileDisplay();

                MessageBox.Show(
                    $"Đã tải về và phân tích cấu trúc thành công từ Google Docs:\n\"{tree.DocumentTitle}\"\n\n" +
                    $"• Phông chữ thân bài: {tree.BodyStyle.FontName} {tree.BodyStyle.FontSize:0.#}pt\n" +
                    $"• Giãn dòng: {tree.BodyStyle.LineSpacing:0.##}\n" +
                    $"• Căn lề: {tree.BodyStyle.Alignment}\n\n" +
                    $"Hồ sơ đã được lưu và kích hoạt!",
                    "Đồng bộ Google Docs thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _autoStatusLabel.Text = $"Lỗi: {ex.Message}";
                MessageBox.Show($"Không thể đồng bộ tự động từ Google Docs:\n{ex.Message}\n\nBạn có thể tải file .docx thủ công từ Docs (Tệp -> Tải xuống -> .docx) rồi kéo vào Chế độ Thủ công.",
                    "Lỗi đồng bộ Docs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _autoFetchButton.Enabled = true;
                Cursor = Cursors.Default;
            }
        }
    }
}
