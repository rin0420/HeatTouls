"""Win32 APIの薄いラッパー。ctypesのみを使い、外部依存を持たない。

提供するもの:
    - foreground_app(): 現在の前面アプリの実行ファイルパスとウィンドウタイトル
    - idle_seconds(): 最後のキーボード/マウス入力からの経過秒数
    - file_description(): exeのバージョン情報から表示名(例: "Google Chrome")
"""

import ctypes
from ctypes import wintypes

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
version_dll = ctypes.WinDLL("version", use_last_error=True)

PROCESS_QUERY_LIMITED_INFORMATION = 0x1000

# --- prototypes -----------------------------------------------------------
user32.GetForegroundWindow.restype = wintypes.HWND
user32.GetForegroundWindow.argtypes = []

user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]

user32.GetWindowTextLengthW.restype = ctypes.c_int
user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]

user32.GetWindowTextW.restype = ctypes.c_int
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]

WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
user32.EnumChildWindows.restype = wintypes.BOOL
user32.EnumChildWindows.argtypes = [wintypes.HWND, WNDENUMPROC, wintypes.LPARAM]

kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]

kernel32.CloseHandle.restype = wintypes.BOOL
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]

kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL
kernel32.QueryFullProcessImageNameW.argtypes = [
    wintypes.HANDLE,
    wintypes.DWORD,
    wintypes.LPWSTR,
    ctypes.POINTER(wintypes.DWORD),
]

kernel32.GetTickCount.restype = wintypes.DWORD
kernel32.GetTickCount.argtypes = []

version_dll.GetFileVersionInfoSizeW.restype = wintypes.DWORD
version_dll.GetFileVersionInfoSizeW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.DWORD)]

version_dll.GetFileVersionInfoW.restype = wintypes.BOOL
version_dll.GetFileVersionInfoW.argtypes = [
    wintypes.LPCWSTR,
    wintypes.DWORD,
    wintypes.DWORD,
    ctypes.c_void_p,
]

version_dll.VerQueryValueW.restype = wintypes.BOOL
version_dll.VerQueryValueW.argtypes = [
    ctypes.c_void_p,
    wintypes.LPCWSTR,
    ctypes.POINTER(ctypes.c_void_p),
    ctypes.POINTER(wintypes.UINT),
]


class LASTINPUTINFO(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.UINT), ("dwTime", wintypes.DWORD)]


user32.GetLastInputInfo.restype = wintypes.BOOL
user32.GetLastInputInfo.argtypes = [ctypes.POINTER(LASTINPUTINFO)]


# --- public ---------------------------------------------------------------
def idle_seconds() -> float:
    """最後の入力からの経過秒数。取得に失敗した場合は0.0。"""
    info = LASTINPUTINFO()
    info.cbSize = ctypes.sizeof(LASTINPUTINFO)
    if not user32.GetLastInputInfo(ctypes.byref(info)):
        return 0.0
    # dwTimeはGetTickCount由来の32bit値。約49日でラップするのでマスクして引く。
    elapsed = (kernel32.GetTickCount() - info.dwTime) & 0xFFFFFFFF
    return elapsed / 1000.0


def _pid_of(hwnd) -> int:
    pid = wintypes.DWORD(0)
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    return pid.value


def _exe_path(pid: int):
    if not pid:
        return None
    handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not handle:
        return None
    try:
        size = wintypes.DWORD(32768)
        buf = ctypes.create_unicode_buffer(size.value)
        if kernel32.QueryFullProcessImageNameW(handle, 0, buf, ctypes.byref(size)):
            return buf.value
    finally:
        kernel32.CloseHandle(handle)
    return None


def _window_title(hwnd) -> str:
    length = user32.GetWindowTextLengthW(hwnd)
    if length <= 0:
        return ""
    buf = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(hwnd, buf, length + 1)
    return buf.value


def _real_uwp_pid(hwnd, host_pid: int):
    """ApplicationFrameHostが持つウィンドウから、実体のUWPアプリのPIDを探す。"""
    found = []

    def callback(child, _lparam):
        child_pid = _pid_of(child)
        if child_pid and child_pid != host_pid:
            found.append(child_pid)
            return False  # 見つかったら列挙を打ち切る
        return True

    try:
        user32.EnumChildWindows(hwnd, WNDENUMPROC(callback), 0)
    except OSError:
        return None
    return found[0] if found else None


def foreground_app():
    """(exe_path, window_title) を返す。前面ウィンドウが無ければ None。"""
    hwnd = user32.GetForegroundWindow()
    if not hwnd:
        return None
    pid = _pid_of(hwnd)
    if not pid:
        return None
    path = _exe_path(pid)
    if path and path.lower().endswith("applicationframehost.exe"):
        real_pid = _real_uwp_pid(hwnd, pid)
        if real_pid:
            path = _exe_path(real_pid) or path
    if not path:
        return None
    return path, _window_title(hwnd)


def file_description(path: str):
    """exeのFileDescription(表示名)を返す。取得できなければ None。"""
    try:
        size = version_dll.GetFileVersionInfoSizeW(path, None)
        if not size:
            return None
        buf = ctypes.create_string_buffer(size)
        if not version_dll.GetFileVersionInfoW(path, 0, size, buf):
            return None

        ptr = ctypes.c_void_p()
        length = wintypes.UINT(0)
        if not version_dll.VerQueryValueW(
            buf, "\\VarFileInfo\\Translation", ctypes.byref(ptr), ctypes.byref(length)
        ) or not length.value:
            return None
        langs = ctypes.cast(ptr, ctypes.POINTER(wintypes.WORD))
        lang, codepage = langs[0], langs[1]

        sub_block = f"\\StringFileInfo\\{lang:04x}{codepage:04x}\\FileDescription"
        if not version_dll.VerQueryValueW(
            buf, sub_block, ctypes.byref(ptr), ctypes.byref(length)
        ) or not length.value:
            return None
        text = ctypes.wstring_at(ptr, length.value).split("\x00", 1)[0].strip()
        return text or None
    except (OSError, ValueError):
        return None
