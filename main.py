"""HeatTouls エントリポイント。

    python main.py                 計測しながらダッシュボードを表示(トレイ常駐)
    python main.py --minimized     ウィンドウを出さずトレイのみで起動
    python main.py --track-only    GUIなしで計測だけ行う
    python main.py --demo          サンプルデータでUIを確認する(別DB)
    python main.py --autostart on  ログオン時の自動起動を登録
"""

import argparse
import os
import sys
import threading


def _parse_args(argv):
    parser = argparse.ArgumentParser(prog="HeatTouls", description="PC稼働時間の計測と可視化")
    parser.add_argument("--minimized", action="store_true", help="ウィンドウを開かずトレイのみで起動")
    parser.add_argument("--track-only", action="store_true", help="GUIを出さず計測だけ行う")
    parser.add_argument("--no-tray", action="store_true", help="タスクトレイに常駐しない")
    parser.add_argument("--demo", action="store_true", help="サンプルデータ入りの別DBでUIを確認する")
    parser.add_argument("--db", metavar="PATH", help="使用するDBファイルを指定する")
    parser.add_argument("--autostart", choices=["on", "off", "status"], help="自動起動の設定")
    return parser.parse_args(argv)


def _has_console() -> bool:
    """コンソールウィンドウが付いているか(Win32のコンソールハンドルで判定)。"""
    try:
        import ctypes

        return bool(ctypes.windll.kernel32.GetConsoleWindow())
    except Exception:
        return sys.stdout is not None


def _notify(message: str) -> None:
    """コマンドラインからは標準出力へ、exeの直起動時はダイアログで伝える。

    windowedビルドのexeでは sys.stdout がNoneではなくダミーになり print が
    どこにも出ないため、frozenかつコンソール無しの時だけダイアログにする。
    (パイプ経由の実行ではコンソールハンドルが取れないので、frozen判定を先に見る)
    """
    if not getattr(sys, "frozen", False) or _has_console():
        print(message)
        return
    try:
        import tkinter as tk
        from tkinter import messagebox

        root = tk.Tk()
        root.withdraw()
        messagebox.showinfo("HeatTouls", message)
        root.destroy()
    except Exception:
        print(message)


def _handle_autostart(mode: str) -> int:
    from heattouls import autostart

    if mode == "on":
        _notify(f"自動起動を登録しました:\n{autostart.enable()}")
    elif mode == "off":
        _notify("自動起動を解除しました。" if autostart.disable() else "自動起動は登録されていません。")
    else:
        current = autostart.status()
        _notify(f"登録済み:\n{current}" if current else "自動起動は登録されていません。")
    return 0


def _run_demo() -> int:
    from heattouls import app as app_module, config, db, demo

    demo_path = config.data_dir() / "demo.db"
    conn = db.connect(demo_path)
    if db.is_empty(conn):
        print(f"サンプルデータを生成しています: {demo_path}")
        demo.seed(conn)
    dashboard = app_module.Dashboard(conn=conn, title="HeatTouls (デモデータ)")
    dashboard.run()
    return 0


def _run_tracker_only(stop_event) -> int:
    from heattouls.tracker import Tracker

    worker = Tracker(stop_event=stop_event)
    worker.start()
    print("計測中です。Ctrl+C で終了します。")
    try:
        while worker.is_alive():
            worker.join(1.0)
    except KeyboardInterrupt:
        pass
    finally:
        stop_event.set()
        worker.join(timeout=5.0)
    return 0


def main(argv=None) -> int:
    args = _parse_args(argv if argv is not None else sys.argv[1:])

    if args.db:
        os.environ["HEATTOULS_DB"] = args.db
    if args.autostart:
        return _handle_autostart(args.autostart)
    if args.demo:
        return _run_demo()

    from heattouls import app as app_module, db, tray
    from heattouls.tracker import Tracker

    stop_event = threading.Event()
    if args.track_only:
        return _run_tracker_only(stop_event)

    worker = Tracker(stop_event=stop_event)
    worker.start()

    dashboard = app_module.Dashboard(conn=db.connect(), minimize_on_close=not args.no_tray)

    icon = None
    if not args.no_tray:
        icon = tray.create(on_show=dashboard.request_show, on_quit=dashboard.request_quit)
        if icon is not None:
            threading.Thread(target=icon.run, name="heattouls-tray", daemon=True).start()
        else:
            # pystrayが無い環境ではトレイに隠せないので、閉じる=終了に戻す。
            dashboard.minimize_on_close = False
            _notify("pystrayが見つからないため、トレイ常駐なしで起動します。")

    if args.minimized:
        dashboard.root.withdraw()

    try:
        dashboard.run()
    finally:
        stop_event.set()
        if icon is not None:
            icon.stop()
        worker.join(timeout=5.0)
    return 0


if __name__ == "__main__":
    sys.exit(main())
