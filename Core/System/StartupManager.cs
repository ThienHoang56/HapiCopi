using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HapiCopi
{
    /// <summary>
    /// Bật/tắt chạy cùng Windows. Bộ cài có sẵn một lựa chọn lúc cài, nhưng sau đó người dùng phải
    /// đổi được từ trong app chứ không phải gỡ ra cài lại.
    /// </summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "HapiCopi";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
                }
                catch (Exception ex)
                {
                    Program.Log($"[Startup] Không đọc được cấu hình chạy cùng Windows: {ex.Message}");
                    return false;
                }
            }
        }

        public static bool TrySet(bool enabled, out string message)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                                        ?? throw new InvalidOperationException("Không mở được khóa Run.");

                if (enabled)
                {
                    key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                }
                else if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }

                message = "";
                Program.Log($"[Startup] Chạy cùng Windows: {(enabled ? "bật" : "tắt")}.");
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Program.Log($"[Startup] Không đổi được cấu hình chạy cùng Windows: {ex.Message}");
                return false;
            }
        }
    }
}
