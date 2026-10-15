using System;
using System.Drawing;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Màu, phông và các control dựng sẵn dùng chung cho mọi cửa sổ.
    ///
    /// Nguyên tắc: không control nào được đặt bằng toạ độ pixel ghi tay. Kích thước chữ thay đổi
    /// theo mức phóng của Windows (100%, 125%, 150%) nên khung chứa phải tự giãn theo chữ, nếu không
    /// nhãn dài sẽ bị cắt trên máy có màn hình phóng to.
    /// </summary>
    public static class UiTheme
    {
        // ============================
        // MÀU
        // ============================

        public static readonly Color Background = Color.FromArgb(248, 250, 252);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 232, 240);

        public static readonly Color TextPrimary = Color.FromArgb(30, 41, 59);
        public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
        public static readonly Color TextDisabled = Color.FromArgb(148, 163, 184);

        public static readonly Color Accent = Color.FromArgb(14, 124, 134);
        public static readonly Color AccentHover = Color.FromArgb(10, 100, 112);
        public static readonly Color AccentLight = Color.FromArgb(230, 244, 245);
        public static readonly Color Success = Color.FromArgb(16, 185, 129);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);

        // ============================
        // PHÔNG
        // ============================

        private const string FamilyName = "Segoe UI";

        public static Font Body => new Font(FamilyName, 9.5f);
        public static Font BodyStrong => new Font(FamilyName, 9.5f, FontStyle.Bold);
        public static Font Small => new Font(FamilyName, 8.75f);
        public static Font SectionTitle => new Font(FamilyName, 10.5f, FontStyle.Bold);
        public static Font PageTitle => new Font(FamilyName, 13f, FontStyle.Bold);

        // ============================
        // BIỂU TƯỢNG
        // ============================

        private static Icon? _appIcon;

        /// <summary>Icon thật của app thay vì hình vẽ tay 16x16 (vẽ tay sẽ vỡ ở mức phóng 125%+).</summary>
        public static Icon AppIcon
        {
            get
            {
                if (_appIcon != null)
                {
                    return _appIcon;
                }

                try
                {
                    _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
                }
                catch
                {
                    _appIcon = SystemIcons.Application;
                }

                return _appIcon;
            }
        }

        // ============================
        // KHUNG CỬA SỔ
        // ============================

        /// <summary>Thiết lập nền tảng cho một cửa sổ: tự co giãn theo phông, nền sáng, icon app.</summary>
        public static void ApplyDialog(Form form, string title)
        {
            form.Text = title;
            form.Font = Body;

            // Mấu chốt chống cắt chữ: WinForms nhân toạ độ theo tỉ lệ phông thực tế / phông thiết kế.
            form.AutoScaleMode = AutoScaleMode.Font;
            form.AutoScaleDimensions = new SizeF(7F, 17F);

            form.BackColor = Background;
            form.ForeColor = TextPrimary;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.ShowInTaskbar = false; // Luôn ẩn khỏi Taskbar, ứng dụng chỉ xuất hiện trong Khay hệ thống (System Tray)

            try
            {
                form.Icon = AppIcon;
            }
            catch { }
        }

        /// <summary>
        /// Chốt kích thước tối thiểu bằng đúng chỗ nội dung cần, và không để cửa sổ vượt quá
        /// vùng làm việc của màn hình.
        /// </summary>
        public static void FitToContent(Form form, Control root, int minWidthDip = 0, int minHeightDip = 0)
        {
            form.PerformLayout();

            Size frame = new Size(form.Width - form.ClientSize.Width, form.Height - form.ClientSize.Height);
            Size content = root.PreferredSize;

            int wantedWidth = Math.Max(content.Width, form.LogicalToDeviceUnits(minWidthDip)) + frame.Width;
            int wantedHeight = Math.Max(content.Height, form.LogicalToDeviceUnits(minHeightDip)) + frame.Height;

            Rectangle work = Screen.FromControl(form).WorkingArea;
            int maxWidth = work.Width - form.LogicalToDeviceUnits(40);
            int maxHeight = work.Height - form.LogicalToDeviceUnits(40);

            form.MinimumSize = new Size(
                Math.Min(wantedWidth, maxWidth),
                Math.Min(form.LogicalToDeviceUnits(320) + frame.Height, maxHeight));

            form.Size = new Size(Math.Min(wantedWidth, maxWidth), Math.Min(wantedHeight, maxHeight));
        }

        // ============================
        // CONTROL DỰNG SẴN
        // ============================

        /// <summary>Cột đơn tự giãn: mỗi control thêm vào là một hàng cao vừa đủ nội dung.</summary>
        public static TableLayoutPanel Column(int padding = 0)
        {
            var panel = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent,
                Padding = new Padding(padding)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return panel;
        }

        /// <summary>Thêm một control thành hàng mới, cao vừa đủ nội dung.</summary>
        public static T AddRow<T>(this TableLayoutPanel panel, T control, int bottomGapDip = 8) where T : Control
        {
            control.Margin = new Padding(0, 0, 0, panel.LogicalToDeviceUnits(bottomGapDip));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(control, 0, panel.RowCount);
            panel.RowCount++;
            return control;
        }

        public static Label PageTitleLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = PageTitle,
                ForeColor = TextPrimary,
                AutoSize = true,
                // Không tắt cái này thì dấu & trong tiêu đề biến mất vì bị hiểu là phím tắt.
                UseMnemonic = false
            };
        }

        public static Label SectionHeader(string text, Color color)
        {
            return new Label
            {
                Text = text,
                Font = SectionTitle,
                ForeColor = color,
                AutoSize = true,
                UseMnemonic = false,
                Margin = new Padding(0)
            };
        }

        /// <summary>
        /// Nhãn tự giãn luôn khai báo bề rộng bằng cả câu trên một dòng, nên nếu không chặn lại thì
        /// cửa sổ sẽ bị kéo rộng ra vài nghìn pixel. Nhãn nào cần xuống dòng thì đánh dấu bằng Tag này,
        /// rồi <see cref="ApplyWrapWidth"/> chốt bề rộng thật lúc dựng và mỗi lần đổi kích thước.
        /// </summary>
        public const string WrapTag = "wrap";

        /// <summary>Đoạn mô tả nhiều dòng: tự xuống dòng và tự cao theo nội dung.</summary>
        public static Label Description(string text) => Hint(text, TextMuted);

        public static Label Hint(string text, Color color)
        {
            return new Label
            {
                Text = text,
                Font = Small,
                ForeColor = color,
                AutoSize = true,
                Dock = DockStyle.Fill,
                UseMnemonic = false,
                Tag = WrapTag
            };
        }

        /// <summary>Chốt bề rộng cho mọi nhãn cần xuống dòng nằm trong <paramref name="container"/>.</summary>
        public static void ApplyWrapWidth(Control container, int width)
        {
            width = Math.Max(120, width);

            foreach (Control child in container.Controls)
            {
                if (child is Label label && (label.Tag as string) == WrapTag)
                {
                    label.MaximumSize = new Size(width, 0);
                }
                else if (child.HasChildren)
                {
                    ApplyWrapWidth(child, width);
                }
            }
        }

        public static CheckBox Option(string text)
        {
            return new CheckBox
            {
                Text = text,
                Font = Body,
                ForeColor = TextPrimary,
                AutoSize = true,
                UseMnemonic = false
            };
        }

        /// <summary>Nút chính. AutoSize nên chữ dài tới đâu nút rộng tới đó, không bao giờ bị cắt.</summary>
        public static Button PrimaryButton(string text)
        {
            var button = BaseButton(text);
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            return button;
        }

        public static Button SecondaryButton(string text)
        {
            var button = BaseButton(text);
            button.BackColor = Background;
            button.ForeColor = TextPrimary;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Surface;
            return button;
        }

        private static Button BaseButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Font = Body,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                UseMnemonic = false,
                UseVisualStyleBackColor = false
            };

            button.Padding = new Padding(
                button.LogicalToDeviceUnits(14),
                button.LogicalToDeviceUnits(3),
                button.LogicalToDeviceUnits(14),
                button.LogicalToDeviceUnits(3));

            button.MinimumSize = new Size(button.LogicalToDeviceUnits(92), button.LogicalToDeviceUnits(34));
            return button;
        }

        /// <summary>Hàng nút xếp từ phải sang trái, tự giãn theo chữ.</summary>
        public static FlowLayoutPanel ButtonRow(params Control[] buttons)
        {
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            foreach (Control button in buttons)
            {
                button.Margin = new Padding(row.LogicalToDeviceUnits(8), 0, 0, 0);
                row.Controls.Add(button);
            }

            return row;
        }

        /// <summary>Đường kẻ phân cách mảnh giữa các nhóm cài đặt.</summary>
        public static Control Separator(Control parent)
        {
            return new Panel
            {
                Height = Math.Max(1, parent.LogicalToDeviceUnits(1)),
                Dock = DockStyle.Fill,
                BackColor = Border
            };
        }
    }
}
