using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HapiCopi
{
    /// <summary>
    /// Bộ tối ưu hóa và thu hồi bộ nhớ RAM tự động cho HapiCopi và Chromium WebView2.
    /// </summary>
    public static class MemoryOptimizer
    {
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        private static DateTime _lastTrimTime = DateTime.MinValue;
        private static readonly object _trimLock = new object();
        private static System.Threading.Timer? _idleTimer;
        private static System.Threading.Timer? _debounceTimer;

        /// <summary>
        /// Khởi động bộ giám sát thu hồi RAM định kỳ khi app chạy ngầm (mỗi 5 phút).
        /// </summary>
        public static void StartPeriodicOptimization()
        {
            _idleTimer = new System.Threading.Timer(_ =>
            {
                OptimizeMemory(aggressive: false);
            }, null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Lên lịch thu hồi RAM sau khi thao tác clipboard kết thúc (debounce).
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
        /// Thu dọn rác .NET và giải phóng Working Set về OS cho tiến trình HapiCopi.
        /// </summary>
        public static void OptimizeMemory(bool aggressive = false)
        {
            lock (_trimLock)
            {
                if (!aggressive && (DateTime.Now - _lastTrimTime).TotalSeconds < 3)
                {
                    return;
                }
                _lastTrimTime = DateTime.Now;
            }

            Task.Run(() =>
            {
                try
                {
                    // 1. Kích hoạt GC dọn dẹp các đối tượng chuỗi/ảnh tạm thời
                    GC.Collect(1, GCCollectionMode.Optimized, false);

                    // 2. Thu hồi Working Set của chính tiến trình HapiCopi
                    using (var current = Process.GetCurrentProcess())
                    {
                        EmptyWorkingSet(current.Handle);
                    }
                }
                catch (Exception ex)
                {
                    Program.Log($"[MemoryOptimizer LỖI] {ex.Message}");
                }
            });
        }
    }
}
