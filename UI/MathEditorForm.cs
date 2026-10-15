using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace HapiCopi
{
    /// <summary>
    /// Cửa sổ soạn công thức toán realtime: gõ AsciiMath/LaTeX -> xem trước ngay -> Copy ảnh PNG dán vào Word/Docs,
    /// hoặc chỉnh sửa trực tiếp in-place hai chiều khi chọn ảnh công thức trong Microsoft Word hoặc Google Docs.
    /// </summary>
    public class MathEditorForm : Form
    {
        private readonly Microsoft.Web.WebView2.WinForms.WebView2 _web;
        private readonly Action<string, string> _setClipboard;
        private bool _ready;
        private bool _attaching;
        private bool _isDocsMode;
        private IntPtr _docsSourceWindow = IntPtr.Zero;
        private (string formula, string mode, float fontSize, bool isWord, bool isDocs)? _pendingFormula;
        private Panel? _loadingPanel;
        private bool _isMiniMode = false;

        public MathEditorForm(Action<string, string> setClipboard)
        {
            _setClipboard = setClipboard;

            Text = "HapiCopi - Soạn công thức toán";
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 17F);
            ClientSize = new Size(760, 480);
            MinimumSize = new Size(580, 380);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;

            try { Icon = UiTheme.AppIcon; } catch { }

            DoubleBuffered = true;
            BackColor = Color.FromArgb(248, 250, 252);

            _web = new Microsoft.Web.WebView2.WinForms.WebView2
            {
                Dock = DockStyle.Fill,
                Visible = false,
                DefaultBackgroundColor = Color.FromArgb(248, 250, 252)
            };
            Controls.Add(_web);

            _loadingPanel = CreateLoadingPanel();
            Controls.Add(_loadingPanel);
            _loadingPanel.BringToFront();

            HandwritingProfileService.OnProfileUpdated += (count, label) =>
            {
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (_web.CoreWebView2 != null)
                            {
                                string script = $"if (typeof onInkDistilled === 'function') onInkDistilled({count}, {System.Text.Json.JsonSerializer.Serialize(label)});";
                                _web.CoreWebView2.ExecuteScriptAsync(script);
                            }
                        }));
                    }
                }
                catch { }
            };

            FormClosing += OnFormClosing;
            VisibleChanged += (_, _) =>
            {
                if (Visible)
                {
                    if (_web.CoreWebView2 != null)
                    {
                        try { _web.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal; } catch { }
                    }
                    RequestReady();
                    SyncWordFontSize();
                    PushLicenseAndSponsorStatus();
                    BeginInvoke(new Action(() =>
                    {
                        _web.Focus();
                        _web.CoreWebView2?.ExecuteScriptAsync("if (typeof mathField !== 'undefined' && mathField) { mathField.focus(); }");
                    }));
                }
            };
        }

        public void RequestReady() => _ = EnsureReadyAsync();

        private async Task EnsureReadyAsync()
        {
            if (_ready || _attaching)
            {
                return;
            }

            _attaching = true;
            try
            {
                await WebViewManager.AttachEditorAsync(_web, "matheditor.html", OnWebMessage);

                if (!Visible && _web.CoreWebView2 != null)
                {
                    try { _web.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low; } catch { }
                }
                SyncWordFontSize();

                // Bộ đếm thời gian dự phòng: nếu sau 3.5 giây trang chưa gửi 'ready' thì tự động mở webview
                var fallbackTimer = new System.Windows.Forms.Timer { Interval = 3500 };
                fallbackTimer.Tick += (s, e) =>
                {
                    fallbackTimer.Stop();
                    fallbackTimer.Dispose();
                    HideLoadingScreen();
                };
                fallbackTimer.Start();
            }
            catch (Exception ex)
            {
                _attaching = false;
                Program.Log($"[MathEditor LỖI] Khởi tạo WebView2 thất bại: {ex.Message}");
                if (Visible && Opacity > 0)
                {
                    bool isRuntimeMissing = ex is Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException ||
                                           ex.Message.IndexOf("WebView2", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           ex is System.IO.FileNotFoundException;

                    if (isRuntimeMissing)
                    {
                        var dlgResult = MessageBox.Show(
                            "Máy tính của bạn chưa có Microsoft Edge WebView2 Runtime (thành phần hiển thị bảng vẽ công thức toán).\n\n" +
                            "Bạn có muốn mở trang tải bộ cài tự động từ Microsoft ngay bây giờ không?",
                            "HapiCopi — Cần WebView2 Runtime",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information);

                        if (dlgResult == DialogResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                                    UseShellExecute = true
                                });
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Không mở được soạn công thức:\n" + ex.Message, "HapiCopi",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void HideLoadingScreen()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(HideLoadingScreen));
                return;
            }

            if (_ready) return;
            _ready = true;
            _attaching = false;

            _web.Visible = true;
            _web.BringToFront();
            _web.Focus();

            if (_loadingPanel != null)
            {
                _loadingPanel.Visible = false;
            }

            if (_pendingFormula.HasValue)
            {
                var pending = _pendingFormula.Value;
                _pendingFormula = null;
                SendFormulaToWebView(pending.formula, pending.mode, pending.fontSize, pending.isWord, pending.isDocs);
            }

            string modeStr = _isMiniMode ? "mini" : "fullscreen";
            _web.CoreWebView2?.ExecuteScriptAsync($"if (typeof setDisplayMode === 'function') setDisplayMode('{modeStr}');");
        }

        private Panel CreateLoadingPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var card = new Panel
            {
                Size = new Size(380, 220),
                BackColor = Color.White
            };

            card.Paint += (s, pe) =>
            {
                using var pen = new Pen(Color.FromArgb(226, 232, 240), 1.5f);
                pe.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            void CenterCard()
            {
                card.Location = new Point(
                    Math.Max(10, (panel.Width - card.Width) / 2),
                    Math.Max(10, (panel.Height - card.Height) / 2));
            }
            panel.Resize += (s, e) => CenterCard();

            var picIcon = new PictureBox
            {
                Size = new Size(48, 48),
                Location = new Point((card.Width - 48) / 2, 22),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            try
            {
                picIcon.Image = UiTheme.AppIcon.ToBitmap();
            }
            catch
            {
                try { picIcon.Image = SystemIcons.Application.ToBitmap(); } catch { }
            }

            var lblTitle = new Label
            {
                Text = "HapiCopi — Soạn công thức toán",
                Font = UiTheme.PageTitle,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(card.Width - 20, 26),
                Location = new Point(10, 78)
            };

            var lblSubtitle = new Label
            {
                Text = "Đang nạp công cụ toán học & AI...",
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(card.Width - 20, 20),
                Location = new Point(10, 108)
            };

            var progress = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Size = new Size(260, 6),
                Location = new Point((card.Width - 260) / 2, 140)
            };

            var lblHint = new Label
            {
                Text = "Sẵn sàng hỗ trợ Word, Docs & viết tay thời gian thực",
                Font = UiTheme.Small,
                ForeColor = Color.FromArgb(148, 163, 184),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(card.Width - 20, 18),
                Location = new Point(10, 168)
            };

            card.Controls.Add(picIcon);
            card.Controls.Add(lblTitle);
            card.Controls.Add(lblSubtitle);
            card.Controls.Add(progress);
            card.Controls.Add(lblHint);

            panel.Controls.Add(card);
            CenterCard();

            return panel;
        }

        public void SetDisplayMode(bool isMini)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetDisplayMode(isMini)));
                return;
            }

            _isMiniMode = isMini;
            if (isMini)
            {
                MinimumSize = new Size(580, 380);
                ClientSize = new Size(760, 480);
            }
            else
            {
                MinimumSize = new Size(760, 480);
                ClientSize = new Size(960, 620);
            }

            string modeStr = isMini ? "mini" : "fullscreen";
            if (_ready && _web.CoreWebView2 != null)
            {
                _web.CoreWebView2.ExecuteScriptAsync($"if (typeof setDisplayMode === 'function') setDisplayMode('{modeStr}');");
            }
        }

        public void OpenForWordFormula(WordFormulaInfo info)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OpenForWordFormula(info)));
                return;
            }

            _isDocsMode = false;
            _docsSourceWindow = IntPtr.Zero;

            SetDisplayMode(true);
            EditorWindow.Present(this);
            RequestReady();

            string mode = (info.Formula.Contains("\\") || info.Formula.Contains("{")) ? "latex" : "asciimath";
            SendFormulaToWebView(info.Formula, mode, info.FontSize, isWord: true, isDocs: false);
        }

        public void OpenForFormulaText(string formula, string mode = "latex", float fontSize = 11f, bool isDocs = false, IntPtr sourceWindow = default)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OpenForFormulaText(formula, mode, fontSize, isDocs, sourceWindow)));
                return;
            }

            _isDocsMode = isDocs;
            _docsSourceWindow = sourceWindow;

            SetDisplayMode(isDocs || sourceWindow != IntPtr.Zero);
            EditorWindow.Present(this);
            RequestReady();

            SendFormulaToWebView(formula, mode, fontSize, isWord: false, isDocs: isDocs);
        }

        public void ResetWordMode()
        {
            _isDocsMode = false;
            _docsSourceWindow = IntPtr.Zero;
            SetDisplayMode(false);
            if (_ready && _web.CoreWebView2 != null)
            {
                _web.CoreWebView2.ExecuteScriptAsync(
                    "window._isWordMode = false; window._isDocsMode = false; var b = document.getElementById('copyBtnText'); if (b) b.textContent = 'Chèn vào Word';");
            }
        }

        private void SendFormulaToWebView(string formula, string mode, float fontSize, bool isWord, bool isDocs = false)
        {
            if (!_ready || _web.CoreWebView2 == null)
            {
                _pendingFormula = (formula, mode, fontSize, isWord, isDocs);
                return;
            }

            string safeFormula = System.Text.Json.JsonSerializer.Serialize(formula);
            string safeMode = System.Text.Json.JsonSerializer.Serialize(mode);
            string isWordStr = isWord ? "true" : "false";
            string isDocsStr = isDocs ? "true" : "false";

            _web.CoreWebView2.ExecuteScriptAsync(
                $"if (typeof setFormula === 'function') setFormula({safeFormula}, {safeMode}, {fontSize}, {isWordStr}, {isDocsStr});");
        }

        private void SyncWordFontSize()
        {
            try
            {
                var format = WordInterop.GetCurrentFormatWithTimeout(60);
                if (format.IsFromActiveWord && format.FontSize > 0)
                {
                    int rounded = (int)Math.Round(format.FontSize);
                    if (_ready && _web.CoreWebView2 != null)
                    {
                        _web.CoreWebView2.ExecuteScriptAsync($"if (typeof setFontSize === 'function') setFontSize({rounded});");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[MathEditor] Lỗi đồng bộ cỡ chữ Word: {ex.Message}");
            }
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(json)) return;

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                string type = root.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";

                if (type == "close")
                {
                    Hide();
                    return;
                }

                if (type == "ready")
                {
                    HideLoadingScreen();
                    PushLicenseAndSponsorStatus();
                    return;
                }

                if (type == "openUpgrade")
                {
                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            using var settings = new SettingsForm();
                            settings.ShowDialog(this);
                            PushLicenseAndSponsorStatus();
                        }
                        catch (Exception ex)
                        {
                            Program.Log($"[MathEditor] Lỗi mở Cài đặt: {ex.Message}");
                        }
                    }));
                    return;
                }

                if (type == "openUrl")
                {
                    string url = root.TryGetProperty("url", out var u) ? (u.GetString() ?? "") : "";
                    if (!string.IsNullOrEmpty(url) && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                    return;
                }

                if (type == "expand")
                {
                    BeginInvoke(new Action(() => SetDisplayMode(false)));
                    return;
                }

                if (type == "collapse")
                {
                    BeginInvoke(new Action(() => SetDisplayMode(true)));
                    return;
                }

                if (type == "recognizeInk")
                {
                    string img = root.TryGetProperty("image", out var im) ? (im.GetString() ?? "") : "";
                    string engine = root.TryGetProperty("engine", out var eng) ? (eng.GetString() ?? "ai") : "ai";
                    bool isCloud = engine.Equals("ai", StringComparison.OrdinalIgnoreCase);

                    // Kiểm tra hạn mức sử dụng (50 lượt offline / ngày hoặc gói Cloud)
                    if (!LicenseService.CanRecognize(isCloud, out string limitReason, out int currentRemaining, out int currentMax))
                    {
                        string safeReason = System.Text.Json.JsonSerializer.Serialize(limitReason);
                        string limitScript = $"if (typeof onInkLimitReached === 'function') onInkLimitReached({(isCloud ? "true" : "false")}, {safeReason}, {currentRemaining}, {currentMax});";
                        BeginInvoke(new Action(() => _web.CoreWebView2?.ExecuteScriptAsync(limitScript)));
                        return;
                    }

                    List<List<PointF>>? strokeList = null;
                    if (root.TryGetProperty("strokes", out var strokesElem) && strokesElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        strokeList = new List<List<PointF>>();
                        foreach (var sElem in strokesElem.EnumerateArray())
                        {
                            var pts = new List<PointF>();
                            foreach (var pElem in sElem.EnumerateArray())
                            {
                                float px = pElem.GetProperty("x").GetSingle();
                                float py = pElem.GetProperty("y").GetSingle();
                                pts.Add(new PointF(px, py));
                            }
                            if (pts.Count > 0) strokeList.Add(pts);
                        }
                    }

                    _ = Task.Run(async () =>
                    {
                        var result = await MathVisionService.RecognizeHandwrittenMathAsync(img, engine, strokeList);
                        if (result.success)
                        {
                            LicenseService.ConsumeRecognition(isCloud);
                        }

                        bool isPro = LicenseService.IsPro;
                        int newRemaining = isCloud ? LicenseService.CloudQuotaRemaining : LicenseService.RemainingDailyFreeQuota;

                        string safeLatex = System.Text.Json.JsonSerializer.Serialize(result.latex);
                        string safeCandidates = System.Text.Json.JsonSerializer.Serialize(result.candidates ?? new List<string>());
                        string safeError = System.Text.Json.JsonSerializer.Serialize(result.error);
                        string script = $"if (typeof onInkRecognized === 'function') onInkRecognized({(result.success ? "true" : "false")}, {safeLatex}, {safeCandidates}, {safeError}, {result.elapsedMs}, {newRemaining}, {(isPro ? "true" : "false")}, {(isCloud ? "true" : "false")});";
                        try
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (_web.CoreWebView2 != null)
                                {
                                    _ = _web.CoreWebView2.ExecuteScriptAsync(script);
                                }
                            }));
                        }
                        catch { }
                    });
                    return;
                }

                if (type == "learnInk")
                {
                    string label = root.TryGetProperty("label", out var l) ? (l.GetString() ?? "") : "";
                    string wrongLabel = root.TryGetProperty("wrongLabel", out var wl) ? (wl.GetString() ?? "") : "";
                    List<List<PointF>>? strokeList = null;
                    if (root.TryGetProperty("strokes", out var strokesElem) && strokesElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        strokeList = new List<List<PointF>>();
                        foreach (var sElem in strokesElem.EnumerateArray())
                        {
                            var pts = new List<PointF>();
                            foreach (var pElem in sElem.EnumerateArray())
                            {
                                float px = pElem.GetProperty("x").GetSingle();
                                float py = pElem.GetProperty("y").GetSingle();
                                pts.Add(new PointF(px, py));
                            }
                            if (pts.Count > 0) strokeList.Add(pts);
                        }
                    }

                    if (!string.IsNullOrEmpty(label) && strokeList != null && strokeList.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(wrongLabel) && !wrongLabel.Equals(label, StringComparison.OrdinalIgnoreCase))
                        {
                            HandwritingProfileService.PenalizeWrongPrediction(wrongLabel, strokeList);
                        }

                        bool learned = HandwritingProfileService.LearnSample(label, strokeList);
                        int count = HandwritingProfileService.GetSampleCount();
                        int labelCount = HandwritingProfileService.GetSampleCountForLabel(label);
                        string script = $"if (typeof onInkLearned === 'function') onInkLearned({(learned ? "true" : "false")}, {count}, {System.Text.Json.JsonSerializer.Serialize(label)}, {labelCount});";
                        BeginInvoke(new Action(() => _web.CoreWebView2?.ExecuteScriptAsync(script)));
                    }
                    return;
                }

                if (type == "getInkProfile")
                {
                    int count = HandwritingProfileService.GetSampleCount();
                    string script = $"if (typeof onInkProfileLoaded === 'function') onInkProfileLoaded({count});";
                    BeginInvoke(new Action(() => _web.CoreWebView2?.ExecuteScriptAsync(script)));
                    return;
                }

                if (type == "getProfileSummary")
                {
                    var summary = HandwritingProfileService.GetProfileSummary();
                    int totalCount = HandwritingProfileService.GetSampleCount();
                    string summaryJson = System.Text.Json.JsonSerializer.Serialize(summary);
                    string script = $"if (typeof onProfileSummaryLoaded === 'function') onProfileSummaryLoaded({summaryJson}, {totalCount});";
                    BeginInvoke(new Action(() => _web.CoreWebView2?.ExecuteScriptAsync(script)));
                    return;
                }

                if (type == "deleteLabelProfile")
                {
                    string targetLabel = root.TryGetProperty("label", out var tl) ? (tl.GetString() ?? "") : "";
                    if (!string.IsNullOrEmpty(targetLabel))
                    {
                        HandwritingProfileService.DeleteSamplesForLabel(targetLabel);
                    }
                    var summary = HandwritingProfileService.GetProfileSummary();
                    int totalCount = HandwritingProfileService.GetSampleCount();
                    string summaryJson = System.Text.Json.JsonSerializer.Serialize(summary);
                    string script = $"if (typeof onProfileSummaryLoaded === 'function') onProfileSummaryLoaded({summaryJson}, {totalCount});";
                    BeginInvoke(new Action(() => _web.CoreWebView2?.ExecuteScriptAsync(script)));
                    return;
                }

                if (type == "openProfileFolder")
                {
                    try
                    {
                        string dir = HandwritingProfileService.ProfileDirectory;
                        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dir,
                            UseShellExecute = true
                        });
                        Program.Log($"[MathEditor] Đã mở thư mục hồ sơ: {dir}");
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"[MathEditor] Lỗi mở thư mục hồ sơ: {ex.Message}");
                    }
                    return;
                }

                if (type == "clearInkProfile")
                {
                    HandwritingProfileService.ClearProfile();
                    int count = HandwritingProfileService.GetSampleCount();
                    var summary = HandwritingProfileService.GetProfileSummary();
                    string summaryJson = System.Text.Json.JsonSerializer.Serialize(summary);
                    string script = $"if (typeof onInkProfileLoaded === 'function') onInkProfileLoaded({count}); if (typeof onProfileSummaryLoaded === 'function') onProfileSummaryLoaded({summaryJson}, {count});";
                    BeginInvoke(new Action(() => _web.CoreWebView2?.ExecuteScriptAsync(script)));
                    Program.Log("[MathEditor] Đã xóa hồ sơ nét chữ cá nhân về mặc định.");
                    return;
                }

                if (type == "copy")
                {
                    string png = root.TryGetProperty("png", out var p) ? (p.GetString() ?? "") : "";
                    string source = root.TryGetProperty("source", out var s) ? (s.GetString() ?? "") : "";
                    string widthPt = root.TryGetProperty("widthPt", out var wp) ? (wp.GetString() ?? "") : "";
                    string heightPt = root.TryGetProperty("heightPt", out var hp) ? (hp.GetString() ?? "") : "";
                    string vAlignPt = root.TryGetProperty("verticalAlignPt", out var vp) ? (vp.GetString() ?? "") : "";
                    bool isDisplay = root.TryGetProperty("isDisplay", out var dp) && dp.GetBoolean();
                    bool isWordUpdate = root.TryGetProperty("isWordUpdate", out var wu) && wu.GetBoolean();
                    bool isDocsUpdate = root.TryGetProperty("isDocsUpdate", out var du) && du.GetBoolean();
                    float.TryParse(widthPt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float wPt);
                    float.TryParse(heightPt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float hPt);
                    float.TryParse(vAlignPt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float vPt);

                    if (isWordUpdate && !string.IsNullOrEmpty(png))
                    {
                        bool success = WordInterop.ReplaceSelectedFormulaInWord(png, source, wPt, hPt, vPt);
                        if (success)
                        {
                            Program.Log($"[MathEditor] Đã cập nhật công thức trực tiếp trong Word: {source}");
                            FormulaTracker.Register(png, source, widthPt: wPt, heightPt: hPt);
                            Hide();
                            WordInterop.ActivateWordWindow();
                            return;
                        }
                        else
                        {
                            Program.Log("[MathEditor] Cập nhật trực tiếp vào Word thất bại, chuyển sang fallback copy Clipboard.");
                        }
                    }

                    if (!string.IsNullOrEmpty(png) && png.StartsWith("data:image"))
                    {
                        string style = "";
                        if (!string.IsNullOrEmpty(heightPt)) style += $"height:{heightPt}pt; ";
                        if (!string.IsNullOrEmpty(widthPt)) style += $"width:{widthPt}pt; ";
                        if (isDisplay)
                        {
                            style += "display:block; margin:6pt auto; ";
                        }
                        else if (!string.IsNullOrEmpty(vAlignPt))
                        {
                            style += $"vertical-align:{vAlignPt}pt; ";
                        }
                        else
                        {
                            style += "vertical-align:middle; ";
                        }

                        string safeSource = System.Net.WebUtility.HtmlEncode(source);
                        string fragment = $"<img src=\"{png}\" alt=\"[HapiCopi:Math] {safeSource}\" title=\"[HapiCopi:Math] {safeSource}\" style=\"{style.Trim()}\" />";
                        string cfHtml = MarkdownToWordConverter.PackageAsClipboardHtml(fragment);

                        string clipboardText = source;
                        if (!LicenseService.IsPro && !string.IsNullOrWhiteSpace(source))
                        {
                            if (!source.StartsWith("% Tạo bởi HapiCopi", StringComparison.OrdinalIgnoreCase))
                            {
                                clipboardText = "% Tạo bởi HapiCopi (hapicopi.com)\r\n" + source;
                            }
                        }

                        _setClipboard(clipboardText, cfHtml);
                        FormulaTracker.Register(png, source, widthPt: wPt, heightPt: hPt);
                        Program.Log($"[MathEditor] Đã copy ảnh công thức vào clipboard (Size={heightPt}pt, Display={isDisplay}).");

                        if ((_isDocsMode || isDocsUpdate) && _docsSourceWindow != IntPtr.Zero)
                        {
                            Hide();
                            IntPtr targetWnd = _docsSourceWindow;
                            _isDocsMode = false;
                            _docsSourceWindow = IntPtr.Zero;

                            Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(100);
                                    ActiveWindowHelper.SetForegroundWindow(targetWnd);
                                    await Task.Delay(120);
                                    ClipboardSimulator.SimulateCtrlV();
                                    Program.Log("[MathEditor] Đã tự động dán công thức cập nhật vào Google Docs!");
                                }
                                catch (Exception ex)
                                {
                                    Program.Log($"[MathEditor LỖI] Tự động dán Docs thất bại: {ex.Message}");
                                }
                            });
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[MathEditor LỖI] Xử lý message thất bại: {ex.Message}");
            }
        }

        private void PushLicenseAndSponsorStatus()
        {
            try
            {
                bool isPro = LicenseService.IsPro;
                bool isProLifetime = LicenseService.IsProLifetime;
                bool hasCloud = LicenseService.HasActiveCloud;
                int remainingFree = LicenseService.RemainingDailyFreeQuota;
                int remainingCloud = LicenseService.CloudQuotaRemaining;
                var sponsor = SponsorService.GetActiveSponsor();

                string sponsorJson = System.Text.Json.JsonSerializer.Serialize(sponsor);
                string script = $"if (typeof onLicenseStatusUpdate === 'function') onLicenseStatusUpdate({(isPro ? "true" : "false")}, {(isProLifetime ? "true" : "false")}, {(hasCloud ? "true" : "false")}, {remainingFree}, {remainingCloud}, {sponsorJson});";
                BeginInvoke(new Action(() => _web.CoreWebView2?.ExecuteScriptAsync(script)));
            }
            catch { }
        }
    }
}
