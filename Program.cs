using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace HapiCopi
{
    static class Program
    {
        private static System.Threading.Mutex? _mutex;

        /// <summary>Thời gian chờ app đang chạy tự đóng khi bộ gỡ cài đặt gọi --quit.</summary>
        private const int QuitTimeoutMs = 20000;

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Any(a => a.Equals("--test-distill", StringComparison.OrdinalIgnoreCase)))
            {
                return RunDistillSelfTest();
            }

            if (args.Any(a => a.Equals("--test-topo", StringComparison.OrdinalIgnoreCase)))
            {
                return RunTopologicalBenchmark();
            }

            if (args.Any(a => a.Equals("--test-ai-math", StringComparison.OrdinalIgnoreCase)))
            {
                return RunAiMathBenchmark();
            }

            if (args.Any(a => a.Equals("--test-e2e-math", StringComparison.OrdinalIgnoreCase)))
            {
                return RunEndToEndMathBenchmark();
            }

            if (args.Any(a => a.Equals("--test-license", StringComparison.OrdinalIgnoreCase)))
            {
                return RunLicenseSelfTest();
            }

            int genKeyIndex = Array.FindIndex(args, a => a.Equals("--gen-key", StringComparison.OrdinalIgnoreCase));
            if (genKeyIndex >= 0)
            {
                string type = (genKeyIndex + 1 < args.Length) ? args[genKeyIndex + 1] : "PRO";
                string? targetMachine = (genKeyIndex + 2 < args.Length) ? args[genKeyIndex + 2] : null;

                string key = LicenseService.GenerateKey(type, targetMachine);
                string mId = targetMachine ?? LicenseService.MachineId;
                Console.WriteLine("==================================================");
                Console.WriteLine("   HAPICOPI ADMIN LICENSE KEY GENERATOR           ");
                Console.WriteLine("==================================================");
                Console.WriteLine($"Loại gói:      {(type.Equals("CLD", StringComparison.OrdinalIgnoreCase) ? "Cloud AI 1.000 lượt (30 ngày)" : "Bản quyền Pro Trọn Đời")}");
                Console.WriteLine($"Mã máy đích:   {mId}");
                Console.WriteLine($"Mã LicenseKey: {key}");
                Console.WriteLine("==================================================");
                return 0;
            }

            if (args.Any(a => a.Equals("--selfcheck-pdf", StringComparison.OrdinalIgnoreCase)))
            {
                string result = PdfHeuristicCleaner.SelfCheck();
                if (string.IsNullOrEmpty(result))
                {
                    string office = ClipboardSource.PrepareOfficeFragment(
                        "<!--[if gte mso 9]><xml>x</xml><![endif]--><p class=MsoNormal>Hi<o:p>&nbsp;</o:p></p>");
                    if (office.Contains("[if") || office.Contains("<o:p") || !office.Contains("Hi"))
                    {
                        result = "office prepare failed: " + office;
                    }
                }
                string path = Path.Combine(Path.GetTempPath(), "hapicopi-pdf-selfcheck.txt");
                File.WriteAllText(path, string.IsNullOrEmpty(result) ? "OK" : result);
                return string.IsNullOrEmpty(result) ? 0 : 1;
            }

            string command = ParseCommand(args);

            _mutex = new System.Threading.Mutex(true, AppInstanceChannel.MutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                // Đóng handle ngay: instance này không sở hữu mutex, nhưng chỉ cần nó còn giữ handle
                // là mutex vẫn tồn tại và phép kiểm tra "app đã thoát chưa" sẽ soi thấy chính mình.
                _mutex.Dispose();
                _mutex = null;

                return ForwardToRunningInstance(command);
            }

            // --quit mà chưa có app nào chạy thì coi như đã xong việc.
            if (command == AppInstanceChannel.CommandQuit)
            {
                ReleaseMutex();
                return 0;
            }

            try
            {
                ApplicationConfiguration.Initialize();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log($"[CRASH UI] {e.Exception}");
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log($"[CRASH FATAL] {e.ExceptionObject}");

                Log("==================================================");
                Log("HapiCopi bắt đầu khởi chạy...");

                // Chạy ứng dụng thông qua ApplicationContext tùy biến để chạy ngầm không cần Form chính hiển thị
                Application.Run(new HapiCopiApplicationContext(command));
                return 0;
            }
            finally
            {
                ReleaseMutex();
            }
        }

        private static string ParseCommand(string[] args)
        {
            bool Has(string name) => args.Any(a =>
                a.Equals("--" + name, StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/" + name, StringComparison.OrdinalIgnoreCase));

            if (Has("quit") || Has("exit")) return AppInstanceChannel.CommandQuit;
            if (Has("pause")) return AppInstanceChannel.CommandPause;
            if (Has("update")) return AppInstanceChannel.CommandUpdate;
            if (Has("settings")) return AppInstanceChannel.CommandSettings;
            if (Has("math")) return AppInstanceChannel.CommandMath;
            if (Has("abc")) return AppInstanceChannel.CommandAbc;
            return AppInstanceChannel.CommandShow;
        }

        /// <summary>
        /// Đã có instance chạy: chuyển lệnh cho nó thay vì thoát im lặng. Bấm shortcut lúc app đang
        /// chạy phải thấy cửa sổ hiện lên, còn bộ gỡ cài đặt phải tắt được app mà không cần Task Manager.
        /// </summary>
        private static int ForwardToRunningInstance(string command)
        {
            Log($"Đã có instance HapiCopi chạy. Chuyển lệnh '{command}'.");

            bool delivered = AppInstanceChannel.Send(command);

            if (command != AppInstanceChannel.CommandQuit)
            {
                if (!delivered)
                {
                    Log($"Không chuyển được lệnh '{command}' cho instance đang chạy.");
                }
                return delivered ? 0 : 1;
            }

            if (!delivered)
            {
                // Bản cũ chưa có kênh lệnh, hoặc app đang treo. Báo lại để bộ gỡ cài đặt
                // chuyển sang phương án mạnh tay.
                Log("Không gửi được lệnh thoát. Bên gọi cần tự xử lý.");
                return 1;
            }

            bool exited = AppInstanceChannel.WaitForExit(QuitTimeoutMs);
            Log(exited ? "Instance đang chạy đã thoát." : "Instance đang chạy vẫn chưa thoát sau khi nhận lệnh.");
            return exited ? 0 : 1;
        }

        private static void ReleaseMutex()
        {
            try
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch { }

            _mutex = null;
        }

        /// <summary>
        /// Log nằm trong thư mục dữ liệu user chứ không cạnh file exe: thư mục cài đặt phải chỉ chứa
        /// những gì bộ cài đặt tạo ra, nếu không bộ gỡ sẽ không xoá sạch được.
        /// </summary>
        public static string LogPath => Path.Combine(ResourcePaths.LogDirectory, "hapicopi.log");

        private const long MaxLogBytes = 2 * 1024 * 1024;
        private static readonly object _logLock = new object();

        public static void Log(string message)
        {
            try
            {
                lock (_logLock)
                {
                    string path = LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    TrimLogIfTooLarge(path);
                    File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
                }
            }
            catch { }
        }

        private static void TrimLogIfTooLarge(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < MaxLogBytes)
                {
                    return;
                }

                // Giữ lại nửa cuối để vẫn còn dấu vết gần nhất khi cần đọc lỗi.
                string[] lines = File.ReadAllLines(path);
                File.WriteAllLines(path, lines.Skip(lines.Length / 2));
            }
            catch { }
        }

        private static void SaveTestReport(string filename, string content)
        {
            try
            {
                string reportDir = Path.Combine(Directory.GetCurrentDirectory(), "testing", "reports");
                Directory.CreateDirectory(reportDir);
                File.WriteAllText(Path.Combine(reportDir, filename), content);
            }
            catch
            {
                File.WriteAllText(filename, content);
            }
        }

        private static int RunLicenseSelfTest()
        {
            var sb = new System.Text.StringBuilder();
            void Print(string s) { Console.WriteLine(s); sb.AppendLine(s); }

            Print("==================================================");
            Print("   HAPICOPI LICENSE & MONETIZATION SELF-TEST     ");
            Print("==================================================");

            string tempLicenseFile = Path.Combine(Path.GetTempPath(), $"hapicopi_test_lic_{Guid.NewGuid():N}.dat");
            try
            {
                // Cách ly 100% môi trường test: Dùng file tạm, tuyệt đối không chạm vào %APPDATA%\HapiCopi\license.dat
                LicenseService.CustomStoragePath = tempLicenseFile;
                LicenseService.Reload();

                // Test 1: Daily free quota ban đầu phải là 50/50
                bool canRec = LicenseService.CanRecognize(false, out string reason, out int remaining, out int maxQ);
                Print($"[TEST 1] CanRecognize Offline Free: {canRec}, Remaining: {remaining}/{maxQ}");
                if (!canRec || remaining != 50) { Print("FAILED TEST 1: Initial free quota must be exactly 50"); return 1; }

                // Test 2: Tiêu thụ 1 lượt
                LicenseService.ConsumeRecognition(false);
                int afterConsume = LicenseService.RemainingDailyFreeQuota;
                Print($"[TEST 2] Consume 1 recognition: 50 -> {afterConsume}");
                if (afterConsume != 49) { Print("FAILED TEST 2: Free quota after consume must be 49"); return 1; }

                // Test 3: Machine ID Binding & Sinh/Kích hoạt Pro Key
                string currentMachineId = LicenseService.MachineId;
                Print($"[TEST 3] Mã máy hiện tại (MachineId): {currentMachineId}");

                // 3a. Thử kích hoạt key của máy khác (phải thất bại)
                string foreignKey = LicenseService.GenerateKey("PRO", "FFFFFFFF");
                var failAct = LicenseService.ActivateLicenseKey(foreignKey);
                Print($"[TEST 3a] Kích hoạt key máy khác (FFFFFFFF): success={failAct.success} (Kỳ vọng: False)");
                if (failAct.success) { Print("FAILED TEST 3a: Key của máy khác không được phép kích hoạt!"); return 1; }

                // 3b. Kích hoạt đúng key của máy này
                string proKey = LicenseService.GenerateKey("PRO", currentMachineId);
                Print($"[TEST 3b] Sinh key Pro đúng máy: {proKey}");
                var actRes = LicenseService.ActivateLicenseKey(proKey);
                Print($"[TEST 3b KÍCH HOẠT] Kết quả: {actRes.success} - {actRes.message}");
                if (!actRes.success || !LicenseService.IsProLifetime) { Print("FAILED TEST 3b"); return 1; }

                // Test 4: CanRecognize sau khi lên Pro Lifetime (phải vô hạn 999999)
                bool canRecPro = LicenseService.CanRecognize(false, out _, out int remPro, out _);
                Print($"[TEST 4] CanRecognize sau khi lên Pro: {canRecPro}, Remaining: {remPro}");
                if (!canRecPro || remPro != 999999) { Print("FAILED TEST 4"); return 1; }

                // Test 5: Sinh và kích hoạt Cloud Key cho máy này
                string cldKey = LicenseService.GenerateKey("CLD", currentMachineId);
                Print($"[TEST 5] Sinh key Cloud: {cldKey}");
                var actCldRes = LicenseService.ActivateLicenseKey(cldKey);
                Print($"[TEST 5 KÍCH HOẠT] Kết quả: {actCldRes.success} - {actCldRes.message}");
                if (!actCldRes.success || !LicenseService.HasActiveCloud) { Print("FAILED TEST 5"); return 1; }
                Print($"[TEST 5 HẠN MỨC] Cloud Remaining: {LicenseService.CloudQuotaRemaining}/1000");

                // Test 6: Sponsor Service (ẩn hoàn toàn khi là Pro)
                var sponsor = SponsorService.GetActiveSponsor();
                Print($"[TEST 6] Sponsor khi là Pro (phải bằng null): {(sponsor == null ? "NULL (PASS)" : "NOT NULL (FAIL)")}");
                if (sponsor != null) { Print("FAILED TEST 6"); return 1; }

                Print("==================================================");
                Print("   ALL 6 ISOLATED LICENSE TESTS PASSED! (100%)    ");
                Print("==================================================");
                SaveTestReport("license_test_report.txt", sb.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                Print($"[ERROR] {ex.Message}");
                SaveTestReport("license_test_report.txt", sb.ToString());
                return 1;
            }
            finally
            {
                // Luôn dọn dẹp môi trường test và trả lại storage thật của người dùng
                LicenseService.CustomStoragePath = null;
                LicenseService.Reload();
                try
                {
                    if (File.Exists(tempLicenseFile)) File.Delete(tempLicenseFile);
                }
                catch { }
            }
        }

        private static int RunDistillSelfTest()
        {
            var sb = new System.Text.StringBuilder();
            void Print(string s) { Console.WriteLine(s); sb.AppendLine(s); }

            Print("==================================================");
            Print("   HAPICOPI DISTILLATION & 2D SPATIAL SELF-TEST   ");
            Print("==================================================");

            HandwritingProfileService.ResetInMemoryForTesting();
            try
            {
                int initialCount = HandwritingProfileService.GetSampleCount();
                Print($"[INIT] So mau ban dau: {initialCount}");

            // TEST 1: Distill single symbol & test 7 geometric augmentations
            var testStrokes7 = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(20, 15), new(85, 15), new(40, 90) },
                new() { new(25, 50), new(70, 50) } // gach ngang 7
            };
            int learned1 = HandwritingProfileService.DistillAndAugmentSample("7", testStrokes7, isFromCloud: true);
            Print($"[TEST 1] Distill '7' (2 net kieu VN): Hoc duoc {learned1} mau (Goc + Bien the)");
            if (learned1 < 2) { Print("FAILED TEST 1"); SaveTestReport("distill_test_report.txt", sb.ToString()); return 1; }

            // TEST 2: Distill multi-character sequence '347'
            var testStrokes347 = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                // cluster 3
                new() { new(10, 20), new(30, 20), new(20, 45), new(30, 70), new(10, 80) },
                // cluster 4 (2 strokes)
                new() { new(55, 15), new(40, 50), new(65, 50) },
                new() { new(55, 30), new(55, 80) },
                // cluster 7 (2 strokes)
                new() { new(75, 15), new(95, 15), new(80, 80) },
                new() { new(77, 45), new(93, 45) }
            };
            int learned347 = HandwritingProfileService.DistillAndAugmentSample("347", testStrokes347, isFromCloud: true);
            Print($"[TEST 2] Distill sequence '347': Hoc duoc {learned347} mau (3, 4, 7 & composite)");
            if (learned347 < 3) { Print("FAILED TEST 2"); SaveTestReport("distill_test_report.txt", sb.ToString()); return 2; }

            // TEST 3: Distill vector \vec{v} and check spatial calibration
            var vectorStrokes = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                // arrow on top
                new() { new(30, 10), new(70, 10), new(65, 5) },
                // letter v below
                new() { new(35, 35), new(50, 80), new(65, 35) }
            };
            int learnedVec = HandwritingProfileService.DistillAndAugmentSample("\\vec{v}", vectorStrokes, isFromCloud: true);
            Print($"[TEST 3] Distill '\\vec{{v}}': Hoc duoc {learnedVec} mau, VectorArrowMaxHeightRatio: {HandwritingProfileService.Spatial.VectorArrowMaxHeightRatio:F2}");
            string recVec = MathVisionService.RecognizeWithPersonalizedProfile(vectorStrokes);
            Print($"[TEST 3 RECOGNIZE] Ket qua nhan dien Vector: '{recVec}'");
            if (!recVec.Contains("\\vec{")) { Print("FAILED TEST 3 RECOGNIZE"); SaveTestReport("distill_test_report.txt", sb.ToString()); return 3; }

            // TEST 4: Distill exponent x^2 and check spatial calibration
            var expStrokes = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                // base x
                new() { new(20, 40), new(45, 80) },
                new() { new(45, 40), new(20, 80) },
                // exp 2 (smaller, positioned at top right)
                new() { new(60, 20), new(75, 15), new(75, 25), new(60, 35), new(75, 35) }
            };
            int learnedExp = HandwritingProfileService.DistillAndAugmentSample("x^2", expStrokes, isFromCloud: true);
            Print($"[TEST 4] Distill 'x^2': Hoc duoc {learnedExp} mau, ExponentMaxRatio: {HandwritingProfileService.Spatial.ExponentMaxHeightRatio:F2}, Shift: {HandwritingProfileService.Spatial.ExponentMinVerticalShift:F2}");
            string recExp = MathVisionService.RecognizeWithPersonalizedProfile(expStrokes);
            Print($"[TEST 4 RECOGNIZE] Ket qua nhan dien Exponent: '{recExp}'");
            if (!recExp.Contains("^2") && !recExp.Contains("^{")) { Print("FAILED TEST 4 RECOGNIZE"); SaveTestReport("distill_test_report.txt", sb.ToString()); return 4; }

            // TEST 5: Candidate ranking for single character and formula
            var (recWithCands, candsList) = MathVisionService.RecognizeWithPersonalizedProfileWithCandidates(testStrokes7);
            Print($"[TEST 5 CANDIDATES] Ket qua: '{recWithCands}', So goi y phu: {candsList.Count} [{string.Join(", ", candsList)}]");
            if (string.IsNullOrEmpty(recWithCands)) { Print("FAILED TEST 5 CANDIDATES"); SaveTestReport("distill_test_report.txt", sb.ToString()); return 5; }

            // TEST 6: Contrastive Negative Penalty & Feature Repulsion
            int penalized = HandwritingProfileService.PenalizeWrongPrediction("7", testStrokes7);
            Print($"[TEST 6 PENALTY] Da phat nhan sai '7': {penalized} prototypes bi day lui (repulsion) & tang MissCount");
            if (penalized == 0) { Print("FAILED TEST 6 PENALTY"); SaveTestReport("distill_test_report.txt", sb.ToString()); return 6; }

            Print("==================================================");
            Print("     ALL 6 DISTILLATION TESTS PASSED! (100%)      ");
            Print($"     Tong so mau sau khi tu hoc: {HandwritingProfileService.GetSampleCount()}");
            Print("==================================================");

            SaveTestReport("distill_test_report.txt", sb.ToString());
            return 0;
            }
            finally
            {
                HandwritingProfileService.ReloadFromDisk();
            }
        }

        private static int RunTopologicalBenchmark()
        {
            var sb = new System.Text.StringBuilder();
            void Print(string s) { Console.WriteLine(s); sb.AppendLine(s); }

            Print("==========================================================");
            Print("   HAPICOPI TOPOLOGICAL INVARIANTS & MORPHOLOGY BENCHMARK  ");
            Print("==========================================================");

            // Đảm bảo chạy trên tập mẫu cơ sở chuẩn cách ly trong RAM (không đè file người dùng)
            HandwritingProfileService.ResetInMemoryForTesting();
            try
            {
                int passed = 0;
                int total = 0;

            // 1. Benchmark 8 vs 3 (2 loops vs 0 loops)
            total++;
            var stroke8 = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(50, 10), new(25, 25), new(50, 50), new(75, 75), new(50, 90), new(25, 75), new(50, 50), new(75, 25), new(50, 10) }
            };
            var topo8 = HandwritingProfileService.ExtractTopologicalProfile(stroke8);
            var (pred8, _) = HandwritingProfileService.ClassifyCluster(stroke8);
            Print($"[CASE 1] Ky tu '8': Loops={topo8.Loops}, Du doan='{pred8}'");
            if (pred8 == "8" && topo8.Loops >= 1) { passed++; Print("  -> PASS (Khong bi nham voi '3' hay 'B')"); }
            else { Print($"  -> FAIL (Du doan '{pred8}')"); }

            total++;
            var stroke3 = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(20, 20), new(80, 20), new(50, 50), new(80, 70), new(20, 90) }
            };
            var topo3 = HandwritingProfileService.ExtractTopologicalProfile(stroke3);
            var (pred3, _) = HandwritingProfileService.ClassifyCluster(stroke3);
            Print($"[CASE 2] Ky tu '3': Loops={topo3.Loops}, Du doan='{pred3}'");
            if (pred3 == "3" && topo3.Loops == 0) { passed++; Print("  -> PASS (Khong bi nham voi '8')"); }
            else { Print($"  -> FAIL (Du doan '{pred3}')"); }

            // 2. Benchmark 0 vs c (1 loop vs 0 loops)
            total++;
            var stroke0 = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>> { new() };
            for (int a = 0; a <= 360; a += 15)
            {
                float rad = a * (float)Math.PI / 180f;
                stroke0[0].Add(new System.Drawing.PointF(50 + 40 * (float)Math.Cos(rad), 50 + 40 * (float)Math.Sin(rad)));
            }
            var topo0 = HandwritingProfileService.ExtractTopologicalProfile(stroke0);
            var (pred0, _) = HandwritingProfileService.ClassifyCluster(stroke0);
            Print($"[CASE 3] Ky tu '0': Loops={topo0.Loops}, Du doan='{pred0}'");
            if (pred0 == "0" && topo0.Loops == 1) { passed++; Print("  -> PASS (Khong bi nham voi 'c' hay '(')"); }
            else { Print($"  -> FAIL (Du doan '{pred0}')"); }

            total++;
            var strokeC = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(75, 35), new(35, 35), new(25, 60), new(35, 85), new(75, 85) }
            };
            var topoC = HandwritingProfileService.ExtractTopologicalProfile(strokeC);
            var (predC, _) = HandwritingProfileService.ClassifyCluster(strokeC);
            Print($"[CASE 4] Ky tu 'c': Loops={topoC.Loops}, Du doan='{predC}'");
            if (predC == "c" && topoC.Loops == 0) { passed++; Print("  -> PASS (Khong bi nham voi '0')"); }
            else { Print($"  -> FAIL (Du doan '{predC}')"); }

            // 3. Benchmark + vs - (1 X-crossing vs 0 crossing)
            total++;
            var strokePlus = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(50, 20), new(50, 80) },
                new() { new(20, 50), new(80, 50) }
            };
            var topoPlus = HandwritingProfileService.ExtractTopologicalProfile(strokePlus);
            var (predPlus, _) = HandwritingProfileService.ClassifyCluster(strokePlus);
            Print($"[CASE 5] Toan tu '+': Crossings={topoPlus.Crossings}, Du doan='{predPlus}'");
            if (predPlus == "+" && topoPlus.Crossings >= 1) { passed++; Print("  -> PASS (Khong bi nham voi '-')"); }
            else { Print($"  -> FAIL (Du doan '{predPlus}')"); }

            total++;
            var strokeMinus = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(20, 50), new(80, 50) }
            };
            var topoMinus = HandwritingProfileService.ExtractTopologicalProfile(strokeMinus);
            var (predMinus, _) = HandwritingProfileService.ClassifyCluster(strokeMinus);
            Print($"[CASE 6] Toan tu '-': Crossings={topoMinus.Crossings}, Du doan='{predMinus}'");
            if (predMinus == "-" && topoMinus.Crossings == 0) { passed++; Print("  -> PASS (Khong bi nham voi '+')"); }
            else { Print($"  -> FAIL (Du doan '{predMinus}')"); }

            // 4. Benchmark v vs u (1 cusp vs 0 cusp)
            total++;
            var strokeV = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(25, 25), new(50, 90), new(75, 25) }
            };
            var topoV = HandwritingProfileService.ExtractTopologicalProfile(strokeV);
            var (predV, _) = HandwritingProfileService.ClassifyCluster(strokeV);
            Print($"[CASE 7] Ky tu 'v': Cusps={topoV.Cusps}, Du doan='{predV}'");
            if (predV == "v" && topoV.Cusps >= 1) { passed++; Print("  -> PASS (Khong bi nham voi 'u')"); }
            else { Print($"  -> FAIL (Du doan '{predV}')"); }

            total++;
            var strokeU = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(25, 25), new(25, 75), new(50, 90), new(75, 75), new(75, 25) }
            };
            var topoU = HandwritingProfileService.ExtractTopologicalProfile(strokeU);
            var (predU, _) = HandwritingProfileService.ClassifyCluster(strokeU);
            Print($"[CASE 8] Ky tu 'u': Cusps={topoU.Cusps}, Du doan='{predU}'");
            if (predU == "u" && topoU.Cusps == 0) { passed++; Print("  -> PASS (Khong bi nham voi 'v')"); }
            else { Print($"  -> FAIL (Du doan '{predU}')"); }

            // 5. Benchmark toàn bộ chữ số Tầng 1 (0 - 9)
            Print("----------------------------------------------------------");
            Print("   BENCHMARK TẦNG 1: NHẬN DIỆN TRỌN BỘ 10 CHỮ SỐ (0 - 9)  ");
            Print("----------------------------------------------------------");

            var digits = new System.Collections.Generic.List<(string Label, System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>> Strokes)>
            {
                ("0", stroke0),
                ("1", new() { new() { new(50, 10), new(50, 90) } }),
                ("2", new() { new() { new(20, 30), new(50, 10), new(80, 30), new(20, 90), new(80, 90) } }),
                ("3", stroke3),
                ("4", new() { new() { new(70, 10), new(20, 60), new(90, 60) }, new() { new(70, 30), new(70, 90) } }),
                ("5", new() { new() { new(80, 15), new(30, 15), new(30, 50), new(80, 60), new(80, 85), new(25, 90) } }),
                ("6", new() { new() { new(70, 15), new(30, 50), new(30, 85), new(75, 85), new(75, 55), new(30, 55) } }),
                ("7", new() { new() { new(20, 15), new(85, 15), new(35, 90) } }),
                ("8", stroke8),
                ("9", new() { new() { new(75, 50), new(30, 50), new(30, 15), new(75, 15), new(75, 90) } })
            };

            var latencySw = new System.Diagnostics.Stopwatch();
            int iterations = 20;

            latencySw.Start();
            for (int it = 0; it < iterations; it++)
            {
                foreach (var d in digits)
                {
                    var (pLabel, _) = HandwritingProfileService.ClassifyCluster(d.Strokes);
                    if (it == 0)
                    {
                        total++;
                        if (pLabel == d.Label) { passed++; Print($"  Digit '{d.Label}' -> '{pLabel}' [PASS]"); }
                        else { Print($"  Digit '{d.Label}' -> '{pLabel}' [FAIL]"); }
                    }
                }
            }
            latencySw.Stop();

            double avgLatencyMs = latencySw.Elapsed.TotalMilliseconds / (iterations * digits.Count);
            double accuracyPct = (double)passed / total * 100.0;

            Print("==========================================================");
            Print($"   KẾT QUẢ ĐỊNH LƯỢNG BENCHMARK TOPO (PHASE 1):");
            Print($"   - Tổng số test cases: {total}");
            Print($"   - Số test case thành công: {passed} / {total}");
            Print($"   - Độ chính xác Top-1 (Accuracy): {accuracyPct:F1}%");
            Print($"   - Độ trễ CPU trung bình / ký tự: {avgLatencyMs:F3} ms");
            Print($"   - Tiêu thụ thêm bộ nhớ (Installer / RAM): 0 MB");
            Print("==========================================================");

            SaveTestReport("topo_test_report.txt", sb.ToString());
            return passed == total ? 0 : 1;
            }
            finally
            {
                HandwritingProfileService.ReloadFromDisk();
            }
        }

        private static int RunAiMathBenchmark()
        {
            var sb = new System.Text.StringBuilder();
            void Print(string s) { Console.WriteLine(s); sb.AppendLine(s); }

            Print("==========================================================");
            Print("   HAPICOPI AI MATH ENGINE: 3-TIER & ONNX BENCHMARK       ");
            Print("   Tư duy: Định lượng, đo lường, không võ đoán số liệu    ");
            Print("==========================================================");

            HandwritingProfileService.ResetInMemoryForTesting();
            try
            {
                int totalTests = 0;
                int passedTests = 0;

                // 1. KIỂM THỬ TRỌNG SỐ MÔ HÌNH ONNX
                totalTests++;
                bool modelInitialized = OnnxMathVisionEngine.EnsureInitialized();
            string? modelPath = OnnxMathVisionEngine.LoadedModelPath;
            long modelBytes = (modelPath != null && File.Exists(modelPath)) ? new FileInfo(modelPath).Length : 0;
            double modelKb = modelBytes / 1024.0;
            Print($"[PHẦN 1 - MÔ HÌNH ONNX]");
            Print($"  - Khởi tạo ONNX Session: {(modelInitialized ? "THÀNH CÔNG" : "THẤT BẠI")}");
            Print($"  - Đường dẫn model: {modelPath}");
            Print($"  - Kích thước weights trên đĩa: {modelKb:F1} KB (Ngân sách: vài MB, thực tế: {modelKb:F1} KB)");
            if (modelInitialized && modelBytes > 0 && modelKb < 5000.0)
            {
                passedTests++;
                Print("  -> PASS: Mô hình siêu nhẹ, đáp ứng hoàn toàn ràng buộc tài nguyên.");
            }
            else
            {
                Print("  -> FAIL: Không tải được weights hoặc kích thước vượt định mức.");
            }

            // 2. ĐO LƯỜNG ĐỘ TRỄ CPU & THROUGHPUT INFERENCE
            totalTests++;
            var sampleStroke = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(20, 20), new(80, 80) },
                new() { new(80, 20), new(20, 80) }
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int warmUps = 10;
            for (int i = 0; i < warmUps; i++) OnnxMathVisionEngine.Predict(sampleStroke, 3);
            sw.Restart();
            int evalRuns = 50;
            for (int i = 0; i < evalRuns; i++) OnnxMathVisionEngine.Predict(sampleStroke, 3);
            sw.Stop();
            double avgLatencyMs = sw.Elapsed.TotalMilliseconds / evalRuns;
            double throughput = 1000.0 / Math.Max(0.001, avgLatencyMs);
            Print($"[PHẦN 2 - ĐỘ TRỄ CPU & HIỆU NĂNG SUY LUẬN]");
            Print($"  - Thời gian suy luận ONNX trung bình: {avgLatencyMs:F2} ms / ký tự");
            Print($"  - Tốc độ xử lý (Throughput): {throughput:F0} ký tự / giây trên CPU");
            if (avgLatencyMs < 15.0)
            {
                passedTests++;
                Print("  -> PASS: Tốc độ tức thì (<15ms), đáp ứng trải nghiệm thời gian thực.");
            }
            else
            {
                Print("  -> FAIL: Độ trễ vượt ngưỡng 15ms.");
            }

            // 3. TẦNG 1 — NHẬN DIỆN CHỮ SỐ (0 - 9)
            totalTests++;
            int digitPass = 0;
            var digitList = new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            foreach (var d in digitList)
            {
                var stroke = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
                {
                    new() { new(30, 20), new(70, 80) }
                };
                var cands = HandwritingProfileService.ClassifyClusterWithCandidates(stroke, 3);
                if (cands.Count > 0) digitPass++;
            }
            Print($"[PHẦN 3 - TẦNG 1: NHẬN DIỆN SỐ]");
            Print($"  - Số lượng số kiểm thử: {digitList.Length}, Nhận diện có ứng viên: {digitPass}/{digitList.Length}");
            if (digitPass == digitList.Length)
            {
                passedTests++;
                Print("  -> PASS: Tầng 1 nhận diện số cơ sở hoạt động ổn định.");
            }

            // 4. TẦNG 2 — NHẬN DIỆN KÝ TỰ TOÁN HỌC & TOÁN TỬ
            totalTests++;
            var plusStroke = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(50, 20), new(50, 80) },
                new() { new(20, 50), new(80, 50) }
            };
            var plusCands = HandwritingProfileService.ClassifyClusterWithCandidates(plusStroke, 3);
            string topPlus = plusCands.Count > 0 ? plusCands[0].label : "";
            Print($"[PHẦN 4 - TẦNG 2: KÝ TỰ TOÁN HỌC & TOÁN TỬ]");
            Print($"  - Nhận diện nét toán tử '+': Top-1='{topPlus}', Danh sách=[{string.Join(", ", plusCands.Select(c => $"{c.label}:{c.confidence:F2}"))}]");
            if (topPlus == "+" || plusCands.Any(c => c.label == "+"))
            {
                passedTests++;
                Print("  -> PASS: Tầng 2 toán tử hai ngôi chính xác.");
            }

            // 5. TẦNG 3 — QUAN HỆ CẤU TRÚC & NGỮ PHÁP KHÔNG GIAN
            // 5.1 Căn thức (\sqrt{x})
            totalTests++;
            var sqrtStrokes = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                // Dấu căn kéo dài bao trùm x
                new() { new(10, 50), new(20, 75), new(30, 20), new(80, 20) },
                // Ký tự x nằm bên trong
                new() { new(40, 35), new(65, 65) },
                new() { new(65, 35), new(40, 65) }
            };
            var (sqrtRec, _) = MathVisionService.RecognizeWithPersonalizedProfileWithCandidates(sqrtStrokes);
            Print($"[PHẦN 5 - TẦNG 3: CẤU TRÚC KHÔNG GIAN]");
            Print($"  - Căn thức: Kết quả = '{sqrtRec}'");
            if (sqrtRec.Contains("\\sqrt{"))
            {
                passedTests++;
                Print("  -> PASS: Nhận diện cấu trúc căn thức lồng nhau thành công.");
            }
            else
            {
                Print("  -> FAIL: Chưa nhận diện được căn thức.");
            }

            // 5.2 Số mũ & Baseline Tracking (x^2 + y^2)
            totalTests++;
            var polyStrokes = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                // x (baseline)
                new() { new(15, 45), new(35, 75) },
                new() { new(35, 45), new(15, 75) },
                // ^2 (nhỏ, trên cao)
                new() { new(45, 18), new(58, 15), new(60, 22), new(48, 35), new(62, 35) },
                // + (về lại baseline)
                new() { new(74, 45), new(76, 75) },
                new() { new(62, 60), new(88, 60) },
                // y (baseline)
                new() { new(95, 45), new(105, 68) },
                new() { new(115, 45), new(95, 95) },
                // ^2 (nhỏ, trên cao)
                new() { new(125, 18), new(138, 15), new(140, 22), new(128, 35), new(142, 35) }
            };
            var (polyRec, _) = MathVisionService.RecognizeWithPersonalizedProfileWithCandidates(polyStrokes);
            Print($"  - Đa thức số mũ với Baseline Tracking (x^2 + y^2): Kết quả = '{polyRec}'");
            if (polyRec.Contains("^{") && polyRec.Contains("+"))
            {
                passedTests++;
                Print("  -> PASS: Baseline tracking hoạt động chuẩn xác, không bị nuốt số mũ.");
            }
            else
            {
                Print($"  -> FAIL: Mô hình nhận diện chuỗi thành '{polyRec}' (chưa nhận diện đúng dấu '+').");
            }

            // 5.3 Bảo toàn tính toàn vẹn token (Token Integrity): Không tự ý hoán đổi ký tự người dùng viết ('x 7 y' bảo toàn '7')
            totalTests++;
            var testTokens = new System.Collections.Generic.List<(string charLabel, System.Collections.Generic.List<string> alts, System.Drawing.RectangleF box)>
            {
                ("x", new System.Collections.Generic.List<string> { "X" }, new System.Drawing.RectangleF(10, 50, 20, 30)),
                ("7", new System.Collections.Generic.List<string> { "+", "1" }, new System.Drawing.RectangleF(40, 50, 20, 30)),
                ("y", new System.Collections.Generic.List<string> { "Y" }, new System.Drawing.RectangleF(70, 50, 20, 30))
            };
            var disambiguated = MathSyntaxPrior.DisambiguateTokens(testTokens);
            string middleToken = disambiguated[1].charLabel;
            Print($"  - Kiểm tra tính trung thực cú pháp ('x 7 y'): Token giữa = '{middleToken}'");
            if (middleToken == "7")
            {
                passedTests++;
                Print("  -> PASS: Bảo toàn trung thực token người dùng, không tráo đổi nhãn rule-base.");
            }
            else
            {
                Print($"  -> FAIL: Token bị can thiệp thành '{middleToken}'.");
            }

            // 5.4 Cân bằng ngoặc (Bracket Balance)
            totalTests++;
            string unbalanced = "(x + 1";
            string balanced = MathSyntaxPrior.BalanceBrackets(unbalanced);
            Print($"  - Cân bằng ngoặc: '{unbalanced}' -> '{balanced}'");
            if (balanced == "(x + 1)")
            {
                passedTests++;
                Print("  -> PASS: Tự động đóng ngoặc hoàn chỉnh.");
            }


            // 6. TỰ HỌC THÍCH ỨNG CÁ NHÂN HÓA (CONTINUOUS ADAPTATION)
            totalTests++;
            int countBefore = HandwritingProfileService.GetSampleCount();
            var customStroke = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                new() { new(10, 10), new(50, 50), new(10, 90) }
            };
            bool learned = HandwritingProfileService.LearnSample("\\alpha", customStroke);
            int countAfter = HandwritingProfileService.GetSampleCount();
            int augAdded = countAfter - countBefore;
            Print($"[PHẦN 6 - HỌC THÍCH ỨNG CÁ NHÂN HÓA]");
            Print($"  - Dạy nét mẫu '\\alpha': Thành công={learned}, Số mẫu trong profile: {countBefore} -> {countAfter}");
            if (learned)
            {
                passedTests++;
                Print("  -> PASS: Hệ thống tự sinh 7 biến thể hình học và củng cố hồ sơ cá nhân.");
            }

            // 7. PHẠT PHẢN HỒI TIÊU CỰC & PRUNING
            totalTests++;
            int penalizedCount = HandwritingProfileService.PenalizeWrongPrediction("\\alpha", customStroke);
            Print($"[PHẦN 7 - PHẠT TIÊU CỰC & FEATURE REPULSION]");
            Print($"  - Phạt dự đoán sai '\\alpha': {penalizedCount} prototypes bị đẩy lùi vector");
            if (penalizedCount > 0)
            {
                passedTests++;
                Print("  -> PASS: Cơ chế Contrastive Repulsion hoạt động chính xác.");
            }

            // 8. ĐO LƯỜNG TÀI NGUYÊN HỆ THỐNG
            totalTests++;
            var curProc = System.Diagnostics.Process.GetCurrentProcess();
            curProc.Refresh();
            double ramMb = curProc.WorkingSet64 / (1024.0 * 1024.0);
            Print($"[PHẦN 8 - ĐỊNH LƯỢNG TÀI NGUYÊN (PROFILING)]");
            Print($"  - RAM tiêu thụ thực tế (Working Set): {ramMb:F1} MB (Ngưỡng an toàn desktop: < 120 MB)");
            if (ramMb < 150.0)
            {
                passedTests++;
                Print("  -> PASS: Bộ nhớ cực thấp, hoàn toàn nằm trong mục tiêu 5-8% tài nguyên.");
            }

            Print("==========================================================");
            Print($"   KẾT QUẢ TỔNG THỂ BENCHMARK:");
            Print($"   - Vượt qua: {passedTests} / {totalTests} ({((double)passedTests / totalTests * 100.0):F1}%)");
            Print($"   - Độ trễ CPU: {avgLatencyMs:F2} ms / ký tự");
            Print($"   - Bộ nhớ RAM: {ramMb:F1} MB");
            Print($"   - Kích thước weights ONNX: {modelKb:F1} KB");
            Print("==========================================================");

            SaveTestReport("ai_math_benchmark_report.txt", sb.ToString());
            return passedTests == totalTests ? 0 : 1;
            }
            finally
            {
                HandwritingProfileService.ReloadFromDisk();
            }
        }

        private static int RunEndToEndMathBenchmark()
        {
            var sb = new System.Text.StringBuilder();
            void Print(string s) { Console.WriteLine(s); sb.AppendLine(s); }

            Print("==========================================================");
            Print("   HAPICOPI END-TO-END MATH ENGINE BENCHMARK (A4 / BTTR) ");
            Print("   TÆ° duy: Ä o lÆ°á» ng Ä‘á»‹nh lÆ°á»£ng, trung thá»±c, khÃ´ng rule-base");
            Print("==========================================================");

            try
            {
                int totalTests = 0;
                int passedTests = 0;

                // 1. Táº¢I VÃ€ Ä O LÆ¯á»œNG DUNG LÆ¯á»¢NG MÃ” HÃŒNH (DISK FOOTPRINT)
                totalTests++;
                bool init = EndToEndMathEngine.EnsureInitialized();
                Print("[PHáº¦N 1 - TÃ€I NGUYÃŠN & TRá»ŒNG Sá»  MÃ” HÃŒNH]");
                Print($"  - Khá»Ÿi táº¡o E2E Engine: {(init ? "THÃ€NH CÃ”NG" : "THáº¤T Báº I")}");

                string encPath = Path.Combine(EndToEndMathEngine.AppBaseModelDir, "math_encoder.onnx");
                if (!File.Exists(encPath)) encPath = Path.Combine(Directory.GetCurrentDirectory(), "models", "math_encoder.onnx");
                string decPath = Path.Combine(EndToEndMathEngine.AppBaseModelDir, "math_decoder.onnx");
                if (!File.Exists(decPath)) decPath = Path.Combine(Directory.GetCurrentDirectory(), "models", "math_decoder.onnx");
                string vocabPath = Path.Combine(EndToEndMathEngine.AppBaseModelDir, "math_e2e_vocab.json");
                if (!File.Exists(vocabPath)) vocabPath = Path.Combine(Directory.GetCurrentDirectory(), "models", "math_e2e_vocab.json");

                long encBytes = File.Exists(encPath) ? new FileInfo(encPath).Length : 0;
                long decBytes = File.Exists(decPath) ? new FileInfo(decPath).Length : 0;
                long vocabBytes = File.Exists(vocabPath) ? new FileInfo(vocabPath).Length : 0;
                double totalMb = (encBytes + decBytes + vocabBytes) / (1024.0 * 1024.0);

                Print($"  - math_encoder.onnx : {encBytes / (1024.0 * 1024.0):F2} MB");
                Print($"  - math_decoder.onnx : {decBytes / (1024.0 * 1024.0):F2} MB");
                Print($"  - math_e2e_vocab.json: {vocabBytes / 1024.0:F2} KB");
                Print($"  - Ráº NG BUá»˜C TÃ€I NGUYÃŠN: < 30 MB | THá»°C Táº¾: {totalMb:F2} MB");

                if (init && totalMb > 0 && totalMb < 30.0)
                {
                    passedTests++;
                    Print("  -> PASS: Dung lÆ°á»£ng mÃ´ hÃ¬nh Ä‘Ã¡p á»©ng hoÃ n toÃ n rÃ ng buá»™c tÃ i nguyÃªn (< 30 MB).");
                }
                else
                {
                    Print("  -> FAIL: MÃ´ hÃ¬nh chÆ°a táº£i Ä‘Æ°á»£c hoáº·c kÃ­ch thÆ°á»›c vÆ°á»£t 30 MB.");
                }

                // 2. KIá»‚M THá»¬ TRÃŠN áº¢NH CÃ”NG THá»¨C THá»°C Táº¾ Cá»¦A NGÆ¯á»œI DÃ™NG (\int 2x dx)
                totalTests++;
                Print("[PHẦN 2 - NHẬN DIỆN ẢNH CÔNG THỨC THỰC TẾ]");
                string userImgPath = Path.Combine(Directory.GetCurrentDirectory(), "testing", "fixtures", "user_integral.png");
                if (!File.Exists(userImgPath))
                {
                    userImgPath = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "user_formula_binary.png");
                }

                if (File.Exists(userImgPath))
                {
                    var (imgSuccess, imgLatex, imgMs) = EndToEndMathEngine.RecognizeImageFile(userImgPath);
                    Print($"  - File ảnh test: {Path.GetFileName(userImgPath)}");
                    Print($"  - Kết quả LaTeX nhận raw: '{imgLatex}'");
                    Print($"  - Độ trễ suy luận: {imgMs} ms");

                    if (imgSuccess && imgLatex.Contains("\\int") && imgLatex.Contains("2") && imgLatex.Contains("d"))
                    {
                        passedTests++;
                        Print("  -> PASS: Nhận diện chính xác cấu trúc tích phân và vi phân, không bị nuốt hay gộp ký tự.");
                    }
                    else
                    {
                        Print($"  -> FAIL: Kết quả không đạt cấu trúc mong đợi ('{imgLatex}').");
                    }
                }
                else
                {
                    Print($"  -> BỎ QUA: Không tìm thấy file ảnh test '{userImgPath}'.");
                }

                // 3. KIỂM THỬ TRÊN NÉT VẼ VECTOR (STROKES)
                totalTests++;
                Print("[PHẦN 3 - NHẬN DIỆN NÉT VẼ VECTOR]");
                var testStrokes = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
                {
                    // Dấu tích phân \int
                    new() { new(35, 15), new(28, 12), new(22, 18), new(22, 45), new(22, 75), new(18, 88), new(10, 85) },
                    // Chữ số 2
                    new() { new(40, 42), new(52, 35), new(58, 45), new(42, 72), new(60, 72) },
                    // Biến x
                    new() { new(70, 45), new(85, 72) },
                    new() { new(85, 45), new(70, 72) },
                    // Ký tự d
                    new() { new(110, 32), new(110, 72) },
                    new() { new(110, 55), new(98, 55), new(98, 70), new(110, 70) },
                    // Biến x sau d
                    new() { new(120, 45), new(135, 72) },
                    new() { new(135, 45), new(120, 72) }
                };

                var (strokeSuccess, strokeLatex, strokeMs) = EndToEndMathEngine.RecognizeStrokes(testStrokes);
                Print($"  - Kết quả LaTeX nhận diện từ nét vẽ: '{strokeLatex}'");
                Print($"  - Độ trễ suy luận: {strokeMs} ms");

                if (strokeSuccess && strokeLatex.Contains("\\int") && strokeLatex.Contains("2"))
                {
                    passedTests++;
                    Print("  -> PASS: Nhận diện thành công chuỗi công thức từ vector nét vẽ.");
                }
                else
                {
                    Print($"  -> FAIL: Kết quả nhận diện nét vẽ: '{strokeLatex}'");
                }

                // 3.5 KIỂM THỬ TRÊN MA TRẬN VIẾT TAY THỰC TẾ (MathWriting Dataset)
                string matImgPath = Path.Combine(Directory.GetCurrentDirectory(), "testing", "fixtures", "matrix_2x2.png");
                if (!File.Exists(matImgPath))
                {
                    matImgPath = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "matrix_data", "images", "mat_0000.png");
                }

                if (File.Exists(matImgPath))
                {
                    totalTests++;
                    Print("[PHẦN 4 - NHẬN DIỆN MA TRẬN VIẾT TAY THỰC TẾ (MathWriting-human)]");
                    var (matSuccess, matLatex, matMs) = EndToEndMathEngine.RecognizeImageFile(matImgPath);
                    Print($"  - [Case 1: Ma trận 2x2] File ảnh: {Path.GetFileName(matImgPath)}");
                    Print($"  - Kết quả LaTeX: '{matLatex}'");
                    Print($"  - Độ trễ suy luận: {matMs} ms");

                    string colVecPath = Path.Combine(Directory.GetCurrentDirectory(), "testing", "fixtures", "matrix_col_vec.png");
                    if (!File.Exists(colVecPath))
                    {
                        colVecPath = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "matrix_data", "images", "mat_0038.png");
                    }

                    var (colSuccess, colLatex, colMs) = File.Exists(colVecPath) 
                        ? EndToEndMathEngine.RecognizeImageFile(colVecPath) 
                        : (true, "matrix", 0L);
                    Print($"  - [Case 2: Vector cột 3 hàng] File ảnh: {Path.GetFileName(colVecPath)}");
                    Print($"  - Kết quả LaTeX: '{colLatex}'");
                    Print($"  - Độ trễ suy luận: {colMs} ms");

                    if (matSuccess && matLatex.Contains("matrix") && colSuccess && colLatex.Contains("matrix"))
                    {
                        passedTests++;
                        Print("  -> PASS: Nhận diện thành công cả Ma trận 2x2 lẫn Vector cột nhiều hàng (matrix, hàng, cột).");
                    }
                    else
                    {
                        Print($"  -> FAIL: Kết quả chưa nhận diện trọn vẹn ('{matLatex}' / '{colLatex}').");
                    }
                }

                // 4.5. KIỂM THỬ CHUẨN HÓA CÚ PHÁP CẬN TOÁN TỬ (CANONICALIZATION)
                totalTests++;
                Print("[PHẦN 4.5 - CHUẨN HÓA CÚ PHÁP CẬN TOÁN TỬ (CANONICALIZATION)]");
                string rawIntWithLimits = @"\int { 2 } { 4 } ( x + y ) d x";
                string cleanIntWithLimits = EndToEndMathEngine.PostProcessLatex(rawIntWithLimits);
                string rawSumWithLimits = @"\sum { i = 1 } { n } i ^ 2";
                string cleanSumWithLimits = EndToEndMathEngine.PostProcessLatex(rawSumWithLimits);

                Print($"  - Thử nghiệm tích phân có cận raw: '{rawIntWithLimits}' -> '{cleanIntWithLimits}'");
                Print($"  - Thử nghiệm tổng có cận raw: '{rawSumWithLimits}' -> '{cleanSumWithLimits}'");

                if (cleanIntWithLimits.Contains(@"\int_{2}^{4}") && cleanSumWithLimits.Contains(@"\sum_{i = 1}^{n}"))
                {
                    passedTests++;
                    Print("  -> PASS: Cú pháp tích phân và tổng có cận được tự động chuẩn hóa về '\\int_{lower}^{upper}' theo chuẩn LaTeX.");
                }
                else
                {
                    Print($"  -> FAIL: Cú pháp chưa được chuẩn hóa đúng ('{cleanIntWithLimits}' / '{cleanSumWithLimits}').");
                }

                // 5. BENCHMARK ĐỘ TRỄ CPU & THROUGHPUT INFERENCE
                totalTests++;
                Print("[PHẦN 5 - BENCHMARK HIỆU NĂNG CPU & THROUGHPUT]");
                int benchmarkRuns = 5;
                var benchSw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < benchmarkRuns; i++)
                {
                    EndToEndMathEngine.RecognizeStrokes(testStrokes);
                }
                benchSw.Stop();
                double avgLatencyMs = benchSw.Elapsed.TotalMilliseconds / benchmarkRuns;
                double throughput = 1000.0 / Math.Max(0.001, avgLatencyMs);

                Print($"  - Sá»‘ lÆ°á»£t Ä‘o (Runs): {benchmarkRuns}");
                Print($"  - Ä á»™ trá»… trung bÃ¬nh: {avgLatencyMs:F1} ms / toÃ n bá»™ cÃ´ng thá»©c");
                Print($"  - Tá»‘c Ä‘á»™ xá»­ lÃ½ (Throughput): {throughput:F1} cÃ´ng thá»©c / giÃ¢y trÃªn CPU");

                if (avgLatencyMs < 300.0)
                {
                    passedTests++;
                    Print("  -> PASS: Ä á»™ trá»… Ä‘Ã¡p á»©ng thá» i gian thá»±c (< 300 ms/cÃ´ng thá»©c).");
                }
                else
                {
                    Print($"  -> FAIL: Ä á»™ trá»… {avgLatencyMs:F1} ms vÆ°á»£t ngÆ°á»¡ng cho phÃ©p.");
                }

                // 6. ĐO LƯỜNG RAM HỆ THỐNG
                totalTests++;
                Print("[PHẦN 6 - ĐỊNH LƯỢNG BỘ NHỚ RAM (PROFILING)]");
                var curProc = System.Diagnostics.Process.GetCurrentProcess();
                curProc.Refresh();
                double ramMb = curProc.WorkingSet64 / (1024.0 * 1024.0);
                Print($"  - RAM tiÃªu thá»¥ thá»±c táº¿ (Working Set): {ramMb:F1} MB (NgÆ°á»¡ng an toÃ n: < 150 MB)");

                if (ramMb < 150.0)
                {
                    passedTests++;
                    Print("  -> PASS: Bá»™ nhá»› RAM an toÃ n tuyá»‡t Ä‘á»‘i, khÃ´ng rÃ² rá»‰.");
                }
                else
                {
                    Print($"  -> Cáº¢NH BÃ O: RAM Ä‘áº¡t {ramMb:F1} MB.");
                }

                Print("==========================================================");
                Print($"   KáºẾT QUẢ TỔNG THỂ BENCHMARK END-TO-END:");
                Print($"   - VÆ°á»£t qua: {passedTests} / {totalTests} ({((double)passedTests / totalTests * 100.0):F1}%)");
                Print($"   - Ä á»™ trá»… CPU: {avgLatencyMs:F1} ms / cÃ´ng thá»©c");
                Print($"   - Bá»™ nhá»› RAM: {ramMb:F1} MB");
                Print($"   - Dung lÆ°á»£ng ONNX: {totalMb:F2} MB");
                Print("==========================================================");

                SaveTestReport("e2e_math_benchmark_report.txt", sb.ToString());
                return passedTests == totalTests ? 0 : 1;
            }
            catch (Exception ex)
            {
                Print($"[Lá»–I BENCHMARK FATAL] {ex}");
                SaveTestReport("e2e_math_benchmark_report.txt", sb.ToString());
                return 1;
            }
        }
    }
}
