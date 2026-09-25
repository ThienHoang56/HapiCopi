# Chụp riêng từng cửa sổ theo tiêu đề bằng PrintWindow.
# Không chụp toàn màn hình nên không lọt nội dung riêng tư của người dùng.
param(
    [Parameter(Mandatory = $true)][string]$TitleLike,
    [Parameter(Mandatory = $true)][string]$OutDir
)

Add-Type -AssemblyName System.Drawing

$src = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

public class WinShot
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hWnd, StringBuilder s, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    // Không khai báo DPI-aware thì GetWindowRect trả toạ độ đã bị Windows thu nhỏ, ảnh chụp sẽ mờ và sai kích thước.
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<IntPtr> Find(string processName, string titleLike)
    {
        var found = new List<IntPtr>();
        EnumWindows((h, l) =>
        {
            if (!IsWindowVisible(h)) return true;

            uint pid;
            GetWindowThreadProcessId(h, out pid);
            try
            {
                var p = System.Diagnostics.Process.GetProcessById((int)pid);
                if (!string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { return true; }

            RECT r;
            if (!GetWindowRect(h, out r)) return true;
            if (r.Right - r.Left < 80 || r.Bottom - r.Top < 60) return true;

            found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string Title(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowTextW(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool Save(IntPtr h, string path)
    {
        RECT r;
        if (!GetWindowRect(h, out r)) return false;

        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        using (var bmp = new Bitmap(w, ht))
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            // flag 2 = PW_RENDERFULLCONTENT, cần cho nội dung vẽ bằng DirectComposition
            bool ok = PrintWindow(h, hdc, 2);
            g.ReleaseHdc(hdc);
            if (!ok) return false;
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        return true;
    }
}
'@

Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing, System.Drawing.Primitives

[WinShot]::SetProcessDPIAware() | Out-Null
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$i = 0
foreach ($h in [WinShot]::Find("HapiCopi", $TitleLike)) {
    $title = [WinShot]::Title($h)
    if ($TitleLike -ne "*" -and $title -notlike "*$TitleLike*") { continue }

    $i++
    $safe = ($title -replace '[^\w\-]', '_')
    if ($safe.Length -eq 0) { $safe = "window$i" }
    $path = Join-Path $OutDir "$safe.png"

    if ([WinShot]::Save($h, $path)) {
        $img = [System.Drawing.Image]::FromFile($path)
        "OK  $title  ($($img.Width)x$($img.Height))  -> $path"
        $img.Dispose()
    }
    else {
        "FAIL $title"
    }
}

if ($i -eq 0) { "Khong tim thay cua so nao khop '$TitleLike'." }
