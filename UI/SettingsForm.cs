using System;
using System.Drawing;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Cửa sổ Cài đặt cho phép người dùng nhập/lưu API key và lựa chọn nhà cung cấp AI.
    /// Cấu hình được lưu vào %APPDATA%\HapiCopi\config.env để dùng được trên bản build phân phối.
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly ComboBox _providerCombo;
        private readonly TextBox _geminiKeyBox;
        private readonly TextBox _geminiModelBox;
        private readonly TextBox _openRouterKeyBox;
        private readonly TextBox _openRouterModelBox;
        private readonly CheckBox _showKeysCheck;

        public SettingsForm()
        {
            Text = "HapiCopi - Cài đặt";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 380);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            int labelX = 20;
            int inputX = 20;
            int inputWidth = 480;
            int y = 18;

            var header = new Label
            {
                Text = "Cấu hình AI",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 204, 113),
                AutoSize = true,
                Location = new Point(labelX, y)
            };
            Controls.Add(header);
            y += 38;

            Controls.Add(new Label { Text = "Nhà cung cấp AI", AutoSize = true, Location = new Point(labelX, y) });
            y += 22;
            _providerCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(inputX, y),
                Width = inputWidth
            };
            _providerCombo.Items.AddRange(new object[] { "gemini", "openrouter" });
            Controls.Add(_providerCombo);
            y += 36;

            Controls.Add(new Label { Text = "Gemini API Key", AutoSize = true, Location = new Point(labelX, y) });
            y += 22;
            _geminiKeyBox = new TextBox
            {
                Location = new Point(inputX, y),
                Width = inputWidth,
                UseSystemPasswordChar = true,
                PlaceholderText = "AIza..."
            };
            Controls.Add(_geminiKeyBox);
            y += 32;

            Controls.Add(new Label { Text = "Gemini Model", AutoSize = true, Location = new Point(labelX, y) });
            y += 22;
            _geminiModelBox = new TextBox
            {
                Location = new Point(inputX, y),
                Width = inputWidth,
                PlaceholderText = "gemini-3.5-flash"
            };
            Controls.Add(_geminiModelBox);
            y += 32;

            Controls.Add(new Label { Text = "OpenRouter API Key (tùy chọn)", AutoSize = true, Location = new Point(labelX, y) });
            y += 22;
            _openRouterKeyBox = new TextBox
            {
                Location = new Point(inputX, y),
                Width = inputWidth,
                UseSystemPasswordChar = true,
                PlaceholderText = "sk-or-..."
            };
            Controls.Add(_openRouterKeyBox);
            y += 32;

            Controls.Add(new Label { Text = "OpenRouter Model (tùy chọn)", AutoSize = true, Location = new Point(labelX, y) });
            y += 22;
            _openRouterModelBox = new TextBox
            {
                Location = new Point(inputX, y),
                Width = inputWidth,
                PlaceholderText = "openai/gpt-oss-120b:free"
            };
            Controls.Add(_openRouterModelBox);
            y += 30;

            _showKeysCheck = new CheckBox
            {
                Text = "Hiện API key",
                AutoSize = true,
                Location = new Point(inputX, y)
            };
            _showKeysCheck.CheckedChanged += (s, e) =>
            {
                _geminiKeyBox.UseSystemPasswordChar = !_showKeysCheck.Checked;
                _openRouterKeyBox.UseSystemPasswordChar = !_showKeysCheck.Checked;
            };
            Controls.Add(_showKeysCheck);

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
            string provider = AppConfig.Get("AI_PROVIDER") ?? "gemini";
            _providerCombo.SelectedItem = provider.ToLowerInvariant() == "openrouter" ? "openrouter" : "gemini";
            if (_providerCombo.SelectedIndex < 0)
            {
                _providerCombo.SelectedIndex = 0;
            }

            _geminiKeyBox.Text = AppConfig.Get("GEMINI_API_KEY") ?? "";
            _geminiModelBox.Text = AppConfig.Get("GEMINI_MODEL") ?? "";
            _openRouterKeyBox.Text = AppConfig.Get("OPENROUTER_API_KEY") ?? "";
            _openRouterModelBox.Text = AppConfig.Get("OPENROUTER_MODEL") ?? "";
        }

        private void OnSaveClicked(object? sender, EventArgs e)
        {
            try
            {
                string provider = _providerCombo.SelectedItem?.ToString() ?? "gemini";

                if (provider == "gemini" && string.IsNullOrWhiteSpace(_geminiKeyBox.Text))
                {
                    MessageBox.Show("Vui lòng nhập Gemini API Key để dùng tính năng định dạng AI (Ctrl+M).",
                        "Thiếu API Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (provider == "openrouter" && string.IsNullOrWhiteSpace(_openRouterKeyBox.Text))
                {
                    MessageBox.Show("Vui lòng nhập OpenRouter API Key để dùng tính năng định dạng AI (Ctrl+M).",
                        "Thiếu API Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                AppConfig.Set("AI_PROVIDER", provider);
                AppConfig.Set("GEMINI_API_KEY", _geminiKeyBox.Text.Trim());
                AppConfig.Set("GEMINI_MODEL", _geminiModelBox.Text.Trim());
                AppConfig.Set("OPENROUTER_API_KEY", _openRouterKeyBox.Text.Trim());
                AppConfig.Set("OPENROUTER_MODEL", _openRouterModelBox.Text.Trim());

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
