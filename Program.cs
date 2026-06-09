using System;
using System.Windows.Forms;

namespace HapiCopi
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Log("==================================================");
            Log("HapiCopi bắt đầu khởi chạy...");
            
            // Chạy ứng dụng thông qua ApplicationContext tùy biến để chạy ngầm không cần Form chính hiển thị
            Application.Run(new HapiCopiApplicationContext());
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