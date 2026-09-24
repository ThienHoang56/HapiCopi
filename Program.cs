using System;
using System.Linq;
using System.Windows.Forms;

namespace HapiCopi
{
    static class Program
    {
        private static System.Threading.Mutex? _mutex;

        [STAThread]
        static void Main(string[] args)
        {
            bool openUpdateWindow = args.Any(a =>
                a.Equals("--update", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/update", StringComparison.OrdinalIgnoreCase));

            // Sử dụng Mutex với tên duy nhất để kiểm tra Single Instance
            bool isNewInstance;
            _mutex = new System.Threading.Mutex(true, "Global\\HapiCopi_SingleInstance_Mutex_9912", out isNewInstance);

            if (!isNewInstance)
            {
                // Đã có một instance khác đang chạy, ghi log và thoát ngay lập tức
                Log("Phát hiện instance HapiCopi khác đang hoạt động. Thoát instance mới.");
                
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[HapiCopi] Một phiên bản khác của HapiCopi đang chạy ẩn ở khay hệ thống (System Tray)!");
                Console.WriteLine("[HapiCopi] Vui lòng tắt phiên bản đang chạy ở khay hệ thống trước khi khởi chạy lại bằng 'dotnet run'.\n");
                Console.ResetColor();
                return;
            }

            try
            {
                ApplicationConfiguration.Initialize();
                Log("==================================================");
                Log("HapiCopi bắt đầu khởi chạy...");
                
                // Chạy ứng dụng thông qua ApplicationContext tùy biến để chạy ngầm không cần Form chính hiển thị
                Application.Run(new HapiCopiApplicationContext(openUpdateWindow));
            }
            finally
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }

        public static void Log(string message)
        {
            try
            {
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
            }
            catch {}
        }
    }
}