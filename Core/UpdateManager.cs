using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace HapiCopi
{
    /// <summary>
    /// Cập nhật và quay lui phiên bản.
    ///
    /// Không tự chép đè file: một tiến trình Windows đang chạy thì không ghi đè được chính nó.
    /// Thay vào đó tải bộ cài Inno rồi nhờ một tiến trình phụ chờ HapiCopi thoát hẳn, chạy bộ cài
    /// ở chế độ im lặng và bật lại app. Nhờ vậy cập nhật và quay lui dùng chung một đường.
    /// </summary>
    public static class UpdateManager
    {
        private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <summary>
        /// AppId trong HapiCopi.iss. Dò theo GUID chứ không ghép sẵn tên khóa, vì cú pháp escape của Inno
        /// quyết định khóa thật có mấy dấu ngoặc nhọn ở cuối.
        /// </summary>
        private const string AppGuid = "A8F3C2E1-9B47-4D6A-8E21-7C5B91F0A3D2";

        private const string RunnerFileName = "apply-update.ps1";

        /// <summary>Thư mục giữ các bộ cài đã tải, để quay lui được cả khi không có mạng.</summary>
        public static string CacheDirectory => Path.Combine(ResourcePaths.AppDataRoot, "Updates");

        /// <summary>Nguồn mặc định là thư mục trên máy, để build local xong là thử cập nhật được ngay.</summary>
        public static string DefaultFeed => Path.Combine(ResourcePaths.AppDataRoot, "Releases");

        public static string Feed
        {
            get
            {
                string? configured = AppConfig.Get("UPDATE_FEED");
                return string.IsNullOrWhiteSpace(configured) ? DefaultFeed : configured.Trim();
            }
        }

        public static bool CheckOnStartup =>
            (AppConfig.Get("UPDATE_CHECK_ON_START") ?? "true").Trim().ToLowerInvariant() != "false";

        public static Version CurrentVersion =>
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

        public static string CurrentVersionText => FormatVersion(CurrentVersion);

        /// <summary>
        /// Thư mục cài do bộ cài Inno ghi lại. Null nghĩa là app đang chạy từ bản build, chưa qua bộ cài.
        /// </summary>
        public static string? InstalledLocation
        {
            get
            {
                foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                {
                    try
                    {
                        using var uninstall = root.OpenSubKey(UninstallRoot);
                        if (uninstall == null)
                        {
                            continue;
                        }

                        foreach (string name in uninstall.GetSubKeyNames())
                        {
                            if (name.IndexOf(AppGuid, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }

                            using var entry = uninstall.OpenSubKey(name);
                            if (entry?.GetValue("InstallLocation") is string location &&
                                !string.IsNullOrWhiteSpace(location))
                            {
                                return location.TrimEnd('\\');
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"[Update] Không đọc được thư mục cài: {ex.Message}");
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// App đang chạy từ đúng thư mục mà bộ cài quản lý. Nếu false thì đang chạy bản build,
        /// cập nhật sẽ tạo ra một bản cài riêng chứ không thay thế thư mục đang chạy.
        /// </summary>
        public static bool IsRunningInstalledCopy
        {
            get
            {
                string? installed = InstalledLocation;
                if (installed == null)
                {
                    return false;
                }

                try
                {
                    string current = AppContext.BaseDirectory.TrimEnd('\\');
                    return string.Equals(
                        Path.GetFullPath(current),
                        Path.GetFullPath(installed),
                        StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static Version ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new Version(0, 0, 0);
            }

            string cleaned = text.Trim().TrimStart('v', 'V');
            return Version.TryParse(cleaned, out Version? parsed) ? parsed : new Version(0, 0, 0);
        }

        public static string FormatVersion(Version version) =>
            $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

        // ============================
        // KIỂM TRA
        // ============================

        public static async Task<UpdateCheckResult> CheckAsync(CancellationToken token = default)
        {
            UpdateManifest? manifest = await UpdateFeed.ReadManifestAsync(Feed, token);
            if (manifest == null)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Error = $"Chưa đọc được danh sách phiên bản tại:\n{UpdateFeed.ResolveManifestLocation(Feed)}",
                    CurrentVersion = CurrentVersion
                };
            }

            List<ReleaseInfo> releases = manifest.Releases
                .Where(r => !string.IsNullOrWhiteSpace(r.Version) && !string.IsNullOrWhiteSpace(r.File))
                .OrderByDescending(r => r.ParsedVersion)
                .ToList();

            Classify(releases);

            Version current = CurrentVersion;
            foreach (ReleaseInfo release in releases)
            {
                release.IsCurrent = release.ParsedVersion == current;
                release.IsCached = File.Exists(GetCachedSetupPath(release));
            }

            return new UpdateCheckResult
            {
                Success = true,
                Releases = releases,
                Recommended = releases.FirstOrDefault(r => r.Channel == ReleaseChannel.Recommended),
                Experimental = releases.FirstOrDefault(r => r.Channel == ReleaseChannel.Experimental),
                CurrentVersion = current,
                HasNewer = releases.Count > 0 && releases[0].ParsedVersion > current
            };
        }

        /// <summary>
        /// Gán kênh theo vị trí: bản mới nhất là thử nghiệm, bản liền trước đã chạy được nên là khuyến nghị.
        /// Khi mới chỉ có một bản thì không có gì để đối chiếu, coi đó là bản khuyến nghị.
        /// </summary>
        private static void Classify(List<ReleaseInfo> releases)
        {
            if (releases.Count == 0)
            {
                return;
            }

            if (releases.Count == 1)
            {
                releases[0].Channel = ReleaseChannel.Recommended;
                return;
            }

            releases[0].Channel = ReleaseChannel.Experimental;
            releases[1].Channel = ReleaseChannel.Recommended;
            for (int i = 2; i < releases.Count; i++)
            {
                releases[i].Channel = ReleaseChannel.Older;
            }
        }

        public static string GetCachedSetupPath(ReleaseInfo release) =>
            Path.Combine(CacheDirectory, $"HapiCopi-Setup-{release.Version}.exe");

        // ============================
        // CÀI ĐẶT / QUAY LUI
        // ============================

        /// <summary>
        /// Tải (nếu chưa có sẵn) rồi chạy bộ cài. Trả về true khi tiến trình cài đã được khởi động —
        /// lúc đó phía gọi phải thoát app để bộ cài ghi đè được file.
        /// </summary>
        public static async Task<(bool Started, string Message)> InstallAsync(
            ReleaseInfo release, IProgress<int>? progress, CancellationToken token = default)
        {
            string setupPath = GetCachedSetupPath(release);

            bool ready = File.Exists(setupPath) && await UpdateFeed.VerifyAsync(setupPath, release, token);
            if (ready)
            {
                Program.Log($"[Update] Dùng bộ cài {release.Version} đã có sẵn trong máy.");
                progress?.Report(100);
            }
            else
            {
                if (File.Exists(setupPath))
                {
                    Program.Log($"[Update] Bộ cài {release.Version} trong cache không hợp lệ, tải lại.");
                    try { File.Delete(setupPath); } catch { }
                }

                if (!await UpdateFeed.DownloadAsync(Feed, release, setupPath, progress, token))
                {
                    return (false, "Không tải được bộ cài, hoặc file tải về không khớp mã băm.");
                }
            }

            return LaunchSetup(setupPath, release);
        }

        /// <summary>
        /// Bộ cài sẽ được chạy sau khi app thoát, lúc đó không còn ai báo lỗi được nữa.
        /// Nên phải chắc đây là file chương trình thật trước khi bước qua điểm không quay lại.
        /// </summary>
        private static bool IsExecutable(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return stream.Length > 2 && stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
            }
            catch (Exception ex)
            {
                Program.Log($"[Update] Không đọc được bộ cài: {ex.Message}");
                return false;
            }
        }

        private static (bool Started, string Message) LaunchSetup(string setupPath, ReleaseInfo release)
        {
            if (!IsExecutable(setupPath))
            {
                Program.Log($"[Update] {setupPath} không phải file chương trình hợp lệ.");
                return (false, "File tải về không phải bộ cài hợp lệ. Kiểm tra lại nguồn cập nhật.");
            }

            try
            {
                string runner = WriteRunnerScript();
                string targetDir = InstalledLocation
                                   ?? Path.Combine(
                                       Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                       "Programs", "HapiCopi");
                string appExe = Path.Combine(targetDir, "HapiCopi.exe");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-WindowStyle");
                startInfo.ArgumentList.Add("Hidden");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(runner);
                startInfo.ArgumentList.Add("-TargetPid");
                startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
                startInfo.ArgumentList.Add("-Setup");
                startInfo.ArgumentList.Add(setupPath);
                startInfo.ArgumentList.Add("-TargetDir");
                startInfo.ArgumentList.Add(targetDir);
                startInfo.ArgumentList.Add("-AppExe");
                startInfo.ArgumentList.Add(appExe);
                startInfo.ArgumentList.Add("-FallbackExe");
                startInfo.ArgumentList.Add(Environment.ProcessPath ?? appExe);

                Process.Start(startInfo);
                Program.Log($"[Update] Đã khởi động bộ cài {release.Version} vào {targetDir}.");

                return (true, $"Đang cài phiên bản {release.Version}. HapiCopi sẽ tự khởi động lại.");
            }
            catch (Exception ex)
            {
                Program.Log($"[Update] Không khởi động được bộ cài: {ex.Message}");
                return (false, $"Không khởi động được bộ cài: {ex.Message}");
            }
        }

        /// <summary>
        /// Tiến trình phụ: chờ HapiCopi thoát, chạy bộ cài im lặng, rồi bật lại app.
        /// Phải nằm ngoài tiến trình đang chạy vì Windows khóa file thực thi của tiến trình sống.
        /// </summary>
        private static string WriteRunnerScript()
        {
            Directory.CreateDirectory(CacheDirectory);
            string path = Path.Combine(CacheDirectory, RunnerFileName);

            const string script = @"param(
    [int]$TargetPid,
    [string]$Setup,
    [string]$TargetDir,
    [string]$AppExe,
    [string]$FallbackExe
)

$ErrorActionPreference = 'Stop'

if ($TargetPid -gt 0) {
    Wait-Process -Id $TargetPid -Timeout 60 -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 800

try {
    $arguments = @('/SILENT', '/NORESTART', '/SUPPRESSMSGBOXES', '/NOCANCEL')
    if ($TargetDir) { $arguments += ""/DIR=$TargetDir"" }
    Start-Process -FilePath $Setup -ArgumentList $arguments -Wait
}
catch {
    # Bỏ qua: dù cài hỏng vẫn phải bật lại app ở bước dưới.
}
finally {
    # Tuyệt đối không để người dùng còn lại tay trắng khi bộ cài thất bại.
    $exe = if (Test-Path $AppExe) { $AppExe } elseif (Test-Path $FallbackExe) { $FallbackExe } else { $null }
    if ($exe) { Start-Process -FilePath $exe -ErrorAction SilentlyContinue }
}
";

            File.WriteAllText(path, script);
            return path;
        }
    }
}
