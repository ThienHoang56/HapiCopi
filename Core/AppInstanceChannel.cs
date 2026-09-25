using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace HapiCopi
{
    /// <summary>
    /// Kênh lệnh giữa các instance HapiCopi.
    ///
    /// App chạy ẩn ở khay hệ thống nên không có cửa sổ để Windows gửi WM_CLOSE tới. Instance đang chạy
    /// vì thế mở một named pipe làm kênh nhận lệnh; instance thứ hai (bấm shortcut, bộ gỡ cài đặt gọi
    /// <c>--quit</c>, bộ cài gọi <c>--update</c>) gửi lệnh vào đó thay vì thoát im lặng.
    /// </summary>
    public static class AppInstanceChannel
    {
        public const string CommandShow = "show";
        public const string CommandSettings = "settings";
        public const string CommandUpdate = "update";
        public const string CommandMath = "math";
        public const string CommandAbc = "abc";
        public const string CommandQuit = "quit";
        public const string CommandPing = "ping";

        public const string MutexName = @"Global\HapiCopi_SingleInstance_Mutex_9912";

        private const string ReplyOk = "ok";
        private const string ReplyUnknown = "unknown";

        // Pipe nằm trong namespace toàn máy nên phải kèm tên user để hai tài khoản cùng đăng nhập
        // không giành kênh của nhau (app cài theo từng user).
        private static string PipeName => "HapiCopi.Command." + Environment.UserName;

        private static Func<string, bool>? _handler;
        private static Thread? _serverThread;
        private static volatile bool _stopping;
        private static NamedPipeServerStream? _listening;

        // ============================
        // PHÍA INSTANCE ĐANG CHẠY
        // ============================

        /// <summary>
        /// Mở kênh nhận lệnh. <paramref name="handler"/> được gọi trên thread nền, phải tự đẩy việc
        /// về UI thread và trả lời ngay chứ không chờ việc đó chạy xong.
        /// </summary>
        public static void StartServer(Func<string, bool> handler)
        {
            if (_serverThread != null)
            {
                return;
            }

            _handler = handler;
            _stopping = false;
            _serverThread = new Thread(ServerLoop)
            {
                IsBackground = true,
                Name = "HapiCopi command channel"
            };
            _serverThread.Start();
        }

        public static void StopServer()
        {
            _stopping = true;

            try
            {
                // Đang chặn ở WaitForConnection: chỉ có cách đóng stream mới gỡ được.
                _listening?.Dispose();
            }
            catch { }

            _listening = null;
            _serverThread = null;
        }

        private static void ServerLoop()
        {
            while (!_stopping)
            {
                try
                {
                    var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None);

                    _listening = server;

                    using (server)
                    {
                        server.WaitForConnection();

                        if (_stopping)
                        {
                            return;
                        }

                        ServeOneClient(server);
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (IOException)
                {
                    // Client ngắt giữa chừng: bỏ qua và chờ client kế tiếp.
                }
                catch (Exception ex)
                {
                    Program.Log($"[Channel] Lỗi kênh lệnh: {ex.Message}");
                    Thread.Sleep(500);
                }
            }
        }

        private static void ServeOneClient(NamedPipeServerStream server)
        {
            var reader = new StreamReader(server, new UTF8Encoding(false), false, 512, leaveOpen: true);
            var writer = new StreamWriter(server, new UTF8Encoding(false), 512, leaveOpen: true) { AutoFlush = true };

            string command = (reader.ReadLine() ?? "").Trim().ToLowerInvariant();
            if (command.Length == 0)
            {
                return;
            }

            Program.Log($"[Channel] Nhận lệnh '{command}' từ instance khác.");

            bool handled;
            try
            {
                handled = _handler?.Invoke(command) ?? false;
            }
            catch (Exception ex)
            {
                Program.Log($"[Channel] Lỗi xử lý lệnh '{command}': {ex.Message}");
                handled = false;
            }

            writer.WriteLine(handled ? ReplyOk : ReplyUnknown);

            try { server.WaitForPipeDrain(); } catch { }
        }

        // ============================
        // PHÍA INSTANCE THỨ HAI
        // ============================

        /// <summary>Gửi lệnh cho instance đang chạy. Trả về false nếu không bắt được kênh.</summary>
        public static bool Send(string command, int timeoutMs = 4000)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                client.Connect(timeoutMs);

                var writer = new StreamWriter(client, new UTF8Encoding(false), 512, leaveOpen: true) { AutoFlush = true };
                var reader = new StreamReader(client, new UTF8Encoding(false), false, 512, leaveOpen: true);

                writer.WriteLine(command);
                return (reader.ReadLine() ?? "").Trim() == ReplyOk;
            }
            catch (TimeoutException)
            {
                Program.Log($"[Channel] Không kết nối được kênh lệnh trong {timeoutMs}ms.");
                return false;
            }
            catch (Exception ex)
            {
                Program.Log($"[Channel] Không gửi được lệnh '{command}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Chờ instance đang chạy thoát hẳn. Mutex single-instance chỉ còn tồn tại khi vẫn có tiến
        /// trình giữ handle, nên mở không được nghĩa là app đã đóng.
        /// </summary>
        public static bool WaitForExit(int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                if (!IsRunning())
                {
                    return true;
                }

                Thread.Sleep(250);
            }

            return !IsRunning();
        }

        public static bool IsRunning()
        {
            try
            {
                if (!Mutex.TryOpenExisting(MutexName, out Mutex? existing))
                {
                    return false;
                }

                existing.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                // Mutex tồn tại nhưng thuộc phiên khác: vẫn coi là đang chạy.
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
