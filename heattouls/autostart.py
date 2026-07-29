"""Windowsログオン時の自動起動登録(HKCUのRunキー)。"""

import sys
import winreg
from pathlib import Path

RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
VALUE_NAME = "HeatTouls"
LEGACY_VALUE_NAME = "toulstudio"   # 旧名で登録済みの分を掃除するため


def _command() -> str:
    """コンソールを出さずに起動するコマンドライン。"""
    if getattr(sys, "frozen", False):
        # exeとしてビルド済み。自分自身を起動すればよい。
        return f'"{Path(sys.executable).resolve()}" --minimized'
    pythonw = Path(sys.executable).with_name("pythonw.exe")
    exe = pythonw if pythonw.exists() else Path(sys.executable)
    entry = Path(__file__).resolve().parent.parent / "main.py"
    return f'"{exe}" "{entry}" --minimized'


def status():
    """登録済みならコマンド文字列、未登録ならNone。"""
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
            value, _type = winreg.QueryValueEx(key, VALUE_NAME)
            return value
    except FileNotFoundError:
        return None


def enable() -> str:
    command = _command()
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
        winreg.SetValueEx(key, VALUE_NAME, 0, winreg.REG_SZ, command)
        # 旧名で登録されたままだと二重に起動してしまうので消しておく
        try:
            winreg.DeleteValue(key, LEGACY_VALUE_NAME)
        except FileNotFoundError:
            pass
    return command


def disable() -> bool:
    removed = False
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_SET_VALUE) as key:
            for name in (VALUE_NAME, LEGACY_VALUE_NAME):
                try:
                    winreg.DeleteValue(key, name)
                    removed = True
                except FileNotFoundError:
                    pass
    except FileNotFoundError:
        return False
    return removed
