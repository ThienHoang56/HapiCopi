using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HapiCopi
{
    /// <summary>
    /// Bộ tối ưu hóa và thu hồi bộ nhớ RAM tự động cho HapiCopi và toàn bộ cây tiến trình Chromium WebView2.
    /// </summary>
    public static class MemoryOptimizer
    {
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private static DateTime _lastTrimTime = DateTime.MinValue;
        private static readonly object _trimLock = new object();
        private static System.Threading.Timer? _idleTimer;
        private static System.Threading.Timer? _debounceTimer;

        /// <summary>
        /// Khởi động bộ giám sát thu hồi RAM định kỳ khi app chạy ngầm (mỗi 3 phút).
        /// </summary>
        public static void StartPeriodicOptimization()
        {
            _idleTimer = new System.Threading.Timer(_ =>
            {
                OptimizeMemory(aggressive: false);
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3));
        }

        /// <summary>
        /// Lên lịch thu hồi RAM sau khi thao tác clipboard kết thúc (debounce 2.5 giây).
        /// </summary>
        public static void ScheduleDelayedOptimization(int delayMs = 2500)
        {
            lock (_trimLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new System.Threading.Timer(_ =>
                {
                    OptimizeMemory(aggressive: true);
                }, null, delayMs, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Thu dọn rác .NET và giải phóng Working Set về OS cho cả HapiCopi lẫn các tiến trình msedgewebview2.
        /// </summary>
        public static void OptimizeMemory(bool aggressive = false)
        {
            lock (_trimLock)
            {
                if (!aggressive && (DateTime.Now - _lastTrimTime).TotalSeconds < 2)
                {
                    return;
                }
                _lastTrimTime = DateTime.Now;
            }

            Task.Run(() =>
            {
                try
                {
                    // 1. Dọn rác heap .NET
                    GC.Collect(1, GCCollectionMode.Optimized, false);

                    // 2. Thu hồi Working Set của chính tiến trình HapiCopi
                    using (var current = Process.GetCurrentProcess())
                    {
                        EmptyWorkingSet(current.Handle);
                    }

                    // 3. Thu hồi Working Set của toàn bộ các tiến trình con Chromium WebView2
                    TrimWebViewProcesses();
                }
                catch (Exception ex)
                {
                    Program.Log($"[MemoryOptimizer LỖI] {ex.Message}");
                }
            });
        }

        private static void TrimWebViewProcesses()
        {
            try
            {
                var webviews = Process.GetProcessesByName("msedgewebview2");
                foreach (var p in webviews)
                {
                    try
                    {
                        TrimProcess(p.Id);
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[MemoryOptimizer TrimWebView] {ex.Message}");
            }
        }

        private static bool TrimProcess(int pid)
        {
            IntPtr hProc = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero) return false;
            try
            {
                return EmptyWorkingSet(hProc);
            }
            finally
            {
                CloseHandle(hProc);
            }
        }
    }
}
