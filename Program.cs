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

        private static int RunDistillSelfTest()
        {
            var sb = new System.Text.StringBuilder();
            void Print(string s) { Console.WriteLine(s); sb.AppendLine(s); }

            Print("==================================================");
            Print("   HAPICOPI DISTILLATION & 2D SPATIAL SELF-TEST   ");
            Print("==================================================");

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
            if (learned1 < 2) { Print("FAILED TEST 1"); File.WriteAllText("distill_test_report.txt", sb.ToString()); return 1; }

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
            if (learned347 < 3) { Print("FAILED TEST 2"); File.WriteAllText("distill_test_report.txt", sb.ToString()); return 2; }

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
            if (!recVec.Contains("\\vec{")) { Print("FAILED TEST 3 RECOGNIZE"); File.WriteAllText("distill_test_report.txt", sb.ToString()); return 3; }

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
            if (!recExp.Contains("^2") && !recExp.Contains("^{")) { Print("FAILED TEST 4 RECOGNIZE"); File.WriteAllText("distill_test_report.txt", sb.ToString()); return 4; }

            // TEST 5: Test Matrix 2D grid recognition
            var matrixStrokes = new System.Collections.Generic.List<System.Collections.Generic.List<System.Drawing.PointF>>
            {
                // left tall bracket (
                new() { new(15, 10), new(10, 50), new(15, 90) },
                // row 1: element (15,25) and (35,25) -> "1", "2"
                new() { new(30, 20), new(30, 40) }, // "1"
                new() { new(50, 20), new(60, 20), new(50, 40), new(60, 40) }, // "2"
                // row 2: element (15,70) and (35,70) -> "3", "4"
                new() { new(30, 60), new(40, 60), new(35, 75), new(40, 90) }, // "3"
                new() { new(55, 60), new(50, 80), new(65, 80) }, // "4"
                // right tall bracket )
                new() { new(75, 10), new(80, 50), new(75, 90) }
            };
            string recMatrix = MathVisionService.RecognizeWithPersonalizedProfile(matrixStrokes);
            Print($"[TEST 5 RECOGNIZE] Ket qua nhan dien Ma tran 2x2: '{recMatrix}'");
            if (!recMatrix.Contains("\\begin{pmatrix}")) { Print("FAILED TEST 5 RECOGNIZE"); File.WriteAllText("distill_test_report.txt", sb.ToString()); return 5; }

            // TEST 6: Candidate ranking for single character and formula
            var (recWithCands, candsList) = MathVisionService.RecognizeWithPersonalizedProfileWithCandidates(testStrokes7);
            Print($"[TEST 6 CANDIDATES] Ket qua: '{recWithCands}', So goi y phu: {candsList.Count} [{string.Join(", ", candsList)}]");
            if (string.IsNullOrEmpty(recWithCands)) { Print("FAILED TEST 6 CANDIDATES"); File.WriteAllText("distill_test_report.txt", sb.ToString()); return 6; }

            // TEST 7: Contrastive Negative Penalty & Feature Repulsion
            int penalized = HandwritingProfileService.PenalizeWrongPrediction("7", testStrokes7);
            Print($"[TEST 7 PENALTY] Da phat nhan sai '7': {penalized} prototypes bi day lui (repulsion) & tang MissCount");
            if (penalized == 0) { Print("FAILED TEST 7 PENALTY"); File.WriteAllText("distill_test_report.txt", sb.ToString()); return 7; }

            Print("==================================================");
            Print("     ALL 7 TESTS PASSED SUCCESSFULLY! (100%)     ");
            Print($"     Tong so mau sau khi tu hoc: {HandwritingProfileService.GetSampleCount()}");
            Print("==================================================");

            File.WriteAllText("distill_test_report.txt", sb.ToString());
            return 0;
        }
    }
}
