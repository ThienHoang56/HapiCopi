using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HapiCopi
{
    /// <summary>
    /// Render TikZ/LaTeX thành SVG bằng LaTeX engine cài trên máy (MiKTeX/TeX Live).
    /// Đường: code -> \documentclass{standalone} -> latex (DVI) -> dvisvgm (SVG, glyph thành path).
    /// SVG sau đó được rasterize sang PNG (WebViewManager.RenderSvgToPngAsync) để nhúng vào Word/Docs.
    /// </summary>
    public static class TikzRenderer
    {
        private static bool _detected = false;
        private static string _latexExe = null;   // latex.exe (xuất DVI)
        private static string _dvisvgmExe = null;  // dvisvgm.exe

        public static bool IsTikz(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return false;
            lang = lang.Trim().ToLowerInvariant();
            return lang == "tikz" || lang == "latex-tikz" || lang == "pgfplots";
        }

        public static bool IsAvailable
        {
            get { EnsureDetected(); return _latexExe != null && _dvisvgmExe != null; }
        }

        public static string EngineInfo
        {
            get
            {
                EnsureDetected();
                return (_latexExe != null && _dvisvgmExe != null)
                    ? $"{_latexExe} + {_dvisvgmExe}"
                    : "chưa tìm thấy latex/dvisvgm (cần cài MiKTeX hoặc TeX Live)";
            }
        }

        private static void EnsureDetected()
        {
            if (_detected) return;
            _detected = true;
            _latexExe = FindExe("latex");
            _dvisvgmExe = FindExe("dvisvgm");
            Program.Log($"[TikZ] Dò engine: latex={_latexExe ?? "KHÔNG CÓ"} | dvisvgm={_dvisvgmExe ?? "KHÔNG CÓ"}");
        }

        private static string FindExe(string name)
        {
            // 1) Tìm trong PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string p = Path.Combine(dir.Trim(), name + ".exe");
                    if (File.Exists(p)) return p;
                }
                catch { }
            }

            // 2) Các thư mục cài đặt phổ biến
            var candidates = new List<string>();
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string prog = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            candidates.Add(Path.Combine(localApp, @"Programs\MiKTeX\miktex\bin\x64"));
            candidates.Add(Path.Combine(prog, @"MiKTeX\miktex\bin\x64"));
            candidates.Add(Path.Combine(progX86, @"MiKTeX\miktex\bin"));
            candidates.Add(@"C:\Program Files\MiKTeX\miktex\bin\x64");
            for (int y = DateTime.Now.Year + 1; y >= 2018; y--)
            {
                candidates.Add($@"C:\texlive\{y}\bin\windows");
                candidates.Add($@"C:\texlive\{y}\bin\win32");
            }

            foreach (var dir in candidates)
            {
                try
                {
                    string p = Path.Combine(dir, name + ".exe");
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Biên dịch TikZ/LaTeX ra chuỗi SVG. Trả về "" nếu thiếu engine hoặc biên dịch lỗi.
        /// </summary>
        public static async Task<string> RenderTikzToSvgAsync(string code)
        {
            EnsureDetected();
            if (_latexExe == null || _dvisvgmExe == null)
            {
                Program.Log("[TikZ] Chưa cài LaTeX/dvisvgm — bỏ qua. " + EngineInfo);
                return "";
            }
            if (string.IsNullOrWhiteSpace(code)) return "";

            string tmp = Path.Combine(Path.GetTempPath(), "hapicopi_tikz_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                code = code.Replace("\u00A0", " ");
                string tex = BuildDocument(code);
                string texPath = Path.Combine(tmp, "job.tex");
                await File.WriteAllTextAsync(texPath, tex, new UTF8Encoding(false));

                // MiKTeX: cho phép tự cài package thiếu mà không hỏi (tránh treo lần đầu).
                bool isMiktex = _latexExe.IndexOf("miktex", StringComparison.OrdinalIgnoreCase) >= 0;
                string installerFlag = isMiktex ? " --enable-installer" : "";

                // 1) latex -> DVI (lần đầu có thể lâu vì MiKTeX tải package -> timeout rộng).
                var r1 = await RunAsync(_latexExe,
                    $"-interaction=nonstopmode -halt-on-error{installerFlag} -output-directory=\"{tmp}\" \"{texPath}\"",
                    tmp, 180000);
                string dviPath = Path.Combine(tmp, "job.dvi");
                if (!File.Exists(dviPath))
                {
                    Program.Log("[TikZ] Biên dịch LaTeX THẤT BẠI. Trích log cuối:\n" + Tail(r1.Item2, 1500));
                    return "";
                }

                // 2) dvisvgm -> SVG (--no-fonts: chuyển glyph thành path để SVG tự chứa, rasterize chuẩn).
                string svgPath = Path.Combine(tmp, "job.svg");
                var r2 = await RunAsync(_dvisvgmExe,
                    $"--no-fonts --exact --output=\"{svgPath}\" \"{dviPath}\"",
                    tmp, 90000);
                if (!File.Exists(svgPath))
                {
                    Program.Log("[TikZ] dvisvgm THẤT BẠI. Log cuối:\n" + Tail(r2.Item2, 1000));
                    return "";
                }

                string svg = await File.ReadAllTextAsync(svgPath, Encoding.UTF8);
                Program.Log($"[TikZ] Render thành công. SVG {svg.Length} ký tự.");
                return svg;
            }
            catch (Exception ex)
            {
                Program.Log("[TikZ LỖI] " + ex.Message);
                return "";
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        private static string BuildDocument(string code)
        {
            string lower = code.ToLowerInvariant();

            // Người dùng đã đưa full document -> dùng nguyên văn.
            if (lower.Contains("\\documentclass")) return code;

            var sb = new StringBuilder();
            sb.AppendLine("\\documentclass[dvisvgm,border=2pt]{standalone}");
            sb.AppendLine("\\usepackage{tikz}");
            sb.AppendLine("\\usepackage{pgfplots}");
            sb.AppendLine("\\pgfplotsset{compat=1.18}");
            sb.AppendLine("\\usetikzlibrary{arrows.meta,positioning,calc,shapes.geometric,shapes.misc,patterns,decorations.pathmorphing,decorations.markings,matrix,fit,backgrounds,angles,quotes,intersections,through}");
            sb.AppendLine("\\begin{document}");

            // pgfplots (\begin{axis}) BẮT BUỘC nằm trong tikzpicture, nên chỉ bỏ bọc khi đã có sẵn tikzpicture.
            if (!lower.Contains("\\begin{tikzpicture}"))
            {
                sb.AppendLine("\\begin{tikzpicture}");
                sb.AppendLine(code);
                sb.AppendLine("\\end{tikzpicture}");
            }
            else
            {
                sb.AppendLine(code);
            }

            sb.AppendLine("\\end{document}");
            return sb.ToString();
        }

        private static async Task<Tuple<bool, string>> RunAsync(string exe, string args, string workDir, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var sb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                try { p.Start(); }
                catch (Exception ex) { return Tuple.Create(false, "Không chạy được " + exe + ": " + ex.Message); }

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                bool completed = await Task.Run(() => p.WaitForExit(timeoutMs));
                if (!completed)
                {
                    try { p.Kill(true); } catch { }
                    return Tuple.Create(false, "Quá thời gian (timeout).");
                }
                return Tuple.Create(p.ExitCode == 0, sb.ToString());
            }
        }

        private static string Tail(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(s.Length - n);
        }
    }
}
