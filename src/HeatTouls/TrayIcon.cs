using System.Runtime.InteropServices;

namespace HeatTouls;

/// <summary>
/// タスクトレイ常駐アイコン。Python 版は pystray に任せていたが、WinUI 3 には
/// 相当するものがないので Shell_NotifyIcon を直接呼ぶ。
///
/// メッセージ受け取り用の隠しウィンドウを UI スレッドに作るので、コールバックは
/// そのまま UI スレッドで実行される。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WmApp = 0x8000;
    private const int WmTrayCallback = WmApp + 1;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int WmDestroy = 0x0002;

    private const int NimAdd = 0x0;
    private const int NimModify = 0x1;
    private const int NimDelete = 0x2;
    private const int NifMessage = 0x1;
    private const int NifIcon = 0x2;
    private const int NifTip = 0x4;

    private const int TpmRightButton = 0x0002;
    private const int TpmReturnCmd = 0x0100;
    private const int MfString = 0x0000;
    private const int MfSeparator = 0x0800;

    private const int IdShow = 1;
    private const int IdQuit = 2;

    private static readonly IntPtr HwndMessage = new(-3);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle, IntPtr classAtom, string? windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(IntPtr instance, string exeFileName, uint iconIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr id, string? item);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    private readonly Action _onShow;
    private readonly Action _onQuit;
    private readonly WndProc _wndProc;   // GC されるとコールバックが死ぬので保持する
    private readonly uint _taskbarCreated;
    private readonly string _tooltip;

    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _added;
    private bool _disposed;

    public TrayIcon(string tooltip, Action onShow, Action onQuit)
    {
        _tooltip = tooltip;
        _onShow = onShow;
        _onQuit = onQuit;
        _wndProc = HandleMessage;

        // エクスプローラが再起動するとトレイの中身が消えるので、通知を受けて入れ直す。
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        var instance = GetModuleHandleW(null);
        var className = $"HeatToulsTray_{Environment.ProcessId}";
        var classNamePtr = Marshal.StringToHGlobalUni(className);

        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = classNamePtr,
        };
        var atom = RegisterClassExW(ref wc);
        if (atom == 0)
        {
            Marshal.FreeHGlobal(classNamePtr);
            return;
        }

        _hwnd = CreateWindowExW(0, new IntPtr(atom), null, 0, 0, 0, 0, 0,
            HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
        Marshal.FreeHGlobal(classNamePtr);
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _icon = LoadAppIcon();
        Add();
    }

    /// <summary>exe に埋め込んだアイコンをそのままトレイに使う。</summary>
    private static IntPtr LoadAppIcon()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
        {
            var icon = ExtractIconW(IntPtr.Zero, exe, 0);
            if (icon != IntPtr.Zero && icon != new IntPtr(1))
            {
                return icon;
            }
        }
        return LoadIconW(IntPtr.Zero, new IntPtr(32512));   // IDI_APPLICATION
    }

    private NotifyIconData BuildData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = WmTrayCallback,
        hIcon = _icon,
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void Add()
    {
        var data = BuildData();
        _added = Shell_NotifyIconW(NimAdd, ref data);
        if (!_added)
        {
            // すでに残っている場合があるので、更新でも試す
            Shell_NotifyIconW(NimModify, ref data);
        }
    }

    private IntPtr HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            Add();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case WmTrayCallback:
                switch ((int)lParam)
                {
                    case WmLButtonUp:
                        _onShow();
                        break;
                    case WmRButtonUp:
                        ShowMenu();
                        break;
                }
                return IntPtr.Zero;

            case WmDestroy:
                return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }
        try
        {
            AppendMenuW(menu, MfString, new IntPtr(IdShow), "ダッシュボードを開く");
            AppendMenuW(menu, MfSeparator, IntPtr.Zero, null);
            AppendMenuW(menu, MfString, new IntPtr(IdQuit), "終了");

            // メニューの外をクリックしたときに閉じるには、前面を自分に移す必要がある。
            SetForegroundWindow(_hwnd);
            GetCursorPos(out var cursor);
            var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCmd,
                cursor.X, cursor.Y, 0, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case IdShow:
                    _onShow();
                    break;
                case IdQuit:
                    _onQuit();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_added)
        {
            var data = BuildData();
            Shell_NotifyIconW(NimDelete, ref data);
            _added = false;
        }
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        _icon = IntPtr.Zero;
    }
}
