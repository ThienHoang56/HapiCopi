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
    }
}
