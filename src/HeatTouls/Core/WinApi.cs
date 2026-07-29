using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HeatTouls.Core;

/// <summary>
/// Win32 API の薄いラッパー。
///
/// 提供するもの:
///   - ForegroundApp(): 現在の前面アプリの実行ファイルパスとウィンドウタイトル
///   - IdleSeconds(): 最後のキーボード/マウス入力からの経過秒数
///   - FileDescription(): exeのバージョン情報から表示名(例: "Google Chrome")
/// </summary>
public static class WinApi
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwnd, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(
        IntPtr process, uint flags, StringBuilder buffer, ref uint size);

    // --- public ------------------------------------------------------------

    /// <summary>最後の入力からの経過秒数。取得に失敗した場合は 0.0。</summary>
    public static double IdleSeconds()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return 0.0;
        }
        // dwTime は GetTickCount 由来の32bit値。約49日でラップするので
        // uint のまま引き算してラップを吸収する。
        var elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return elapsed / 1000.0;
    }

    /// <summary>(実行ファイルパス, ウィンドウタイトル)。前面ウィンドウが無ければ null。</summary>
    public static (string ExePath, string Title)? ForegroundApp()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }
        var pid = PidOf(hwnd);
        if (pid == 0)
        {
            return null;
        }

        var path = ExePath(pid);
        if (path is not null && path.EndsWith("applicationframehost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var realPid = RealUwpPid(hwnd, pid);
            if (realPid is not null)
            {
                path = ExePath(realPid.Value) ?? path;
            }
        }
        return path is null ? null : (path, WindowTitle(hwnd));
    }

    /// <summary>exe の FileDescription(表示名)。取得できなければ null。</summary>
    public static string? FileDescription(string path)
    {
        try
        {
            var text = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // --- 内部 --------------------------------------------------------------

    private static uint PidOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    private static string? ExePath(uint pid)
    {
        if (pid == 0)
        {
            return null;
        }
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            uint size = 32768;
            var buffer = new StringBuilder((int)size);
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }
        var buffer = new StringBuilder(length + 1);
        GetWindowTextW(hwnd, buffer, length + 1);
        return buffer.ToString();
    }

    /// <summary>ApplicationFrameHost が持つウィンドウから、実体のUWPアプリのPIDを探す。</summary>
    private static uint? RealUwpPid(IntPtr hwnd, uint hostPid)
    {
        uint? found = null;

        bool Callback(IntPtr child, IntPtr _)
        {
            var childPid = PidOf(child);
            if (childPid != 0 && childPid != hostPid)
            {
                found = childPid;
                return false;   // 見つかったら列挙を打ち切る
            }
            return true;
        }

        try
        {
            EnumChildWindows(hwnd, Callback, IntPtr.Zero);
        }
        catch (Exception)
        {
            return null;
        }
        return found;
    }
}
