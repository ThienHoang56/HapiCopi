using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HapiCopi
{
    /// <summary>
    /// Bộ tối ưu hóa và thu hồi bộ nhớ RAM tự động cho HapiCopi an toàn, không gây khựng hệ điều hành.
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
        /// Khởi động bộ giám sát thu hồi RAM định kỳ khi app chạy ngầm (mỗi 3 phút, bắt đầu sau 3 phút khởi động để không ảnh hưởng startup).
        /// </summary>
        public static void StartPeriodicOptimization()
        {
            _idleTimer = new System.Threading.Timer(_ =>
            {
                OptimizeMemory(aggressive: false);
            }, null, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));
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
        /// Thu dọn rác .NET và giải phóng Working Set về OS cho HapiCopi.
        /// Tuyệt đối không gọi EmptyWorkingSet lên các tiến trình msedgewebview2 toàn hệ thống
        /// để tránh làm nghẽn/lag các phần mềm khác (Teams, Edge, Outlook, Widgets).
        /// </summary>
        public static void OptimizeMemory(bool aggressive = false)
        {
            lock (_trimLock)
            {
                if (!aggressive && (DateTime.Now - _lastTrimTime).TotalSeconds < 30)
                {
                    return;
                }
                _lastTrimTime = DateTime.Now;
            }

            Task.Run(() =>
            {
                try
                {
                    if (aggressive)
                    {
                        // 1. Dọn rác heap .NET (Bao gồm Generation 2 và LOH - Large Object Heap nơi chứa mảng byte và chuỗi Base64 lớn)
                        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

                        // 2. Thu hồi Working Set của chính tiến trình HapiCopi về OS
                        using (var current = Process.GetCurrentProcess())
                        {
                            EmptyWorkingSet(current.Handle);
                        }
                    }
                    else
                    {
                        // Dọn dẹp nhẹ định kỳ: không chặn luồng, không nén heap LOH, không ép EmptyWorkingSet
                        GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
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
