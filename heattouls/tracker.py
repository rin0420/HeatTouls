"""前面ウィンドウを一定間隔で観測し、アクティブ時間を積算するトラッカー。

1秒ごとに「今どのアプリが前面か」「無操作は何秒続いているか」を見て、
アイドルでなければ経過時間をそのアプリの (日, 時) バケットへ足し込む。
バケットは一定間隔でまとめてDBへ書き出す。
"""

import os
import threading
import time
from collections import defaultdict

from . import config, db, winapi


class Tracker(threading.Thread):
    """バックグラウンドで計測を続けるスレッド。"""

    daemon = True

    def __init__(self, db_path=None, stop_event: threading.Event | None = None):
        super().__init__(name="heattouls-tracker")
        self._db_path = db_path
        self._stop_event = stop_event or threading.Event()
        self._conn = None
        self._buffer = defaultdict(float)      # (day, hour, app_id) -> seconds
        self._app_cache = {}                   # exe_path -> app_id
        self._session = None                   # (app_id, start_ts, last_ts)
        self._last_flush = 0.0
        self.last_error = None

    # --- 制御 ------------------------------------------------------------
    def stop(self) -> None:
        self._stop_event.set()

    # --- 内部 ------------------------------------------------------------
    def _resolve_app(self, exe_path: str) -> int:
        cached = self._app_cache.get(exe_path)
        if cached is not None:
            return cached
        name = winapi.file_description(exe_path) or os.path.splitext(os.path.basename(exe_path))[0]
        app_id = db.app_id(self._conn, exe_path, name)
        self._app_cache[exe_path] = app_id
        return app_id

    def _flush(self) -> None:
        if self._buffer:
            rows = [(day, hour, app_id, secs) for (day, hour, app_id), secs in self._buffer.items()]
            db.add_usage(self._conn, rows)
            self._buffer.clear()
        self._last_flush = time.monotonic()

    def _close_session(self) -> None:
        if not self._session:
            return
        app_id, start_ts, last_ts = self._session
        self._session = None
        duration = last_ts - start_ts
        if duration >= config.MIN_SESSION_SECONDS:
            day = time.strftime("%Y-%m-%d", time.localtime(start_ts))
            db.add_session(self._conn, app_id, start_ts, last_ts, day)

    def _touch_session(self, app_id: int, now: float) -> None:
        """セッションを継続 or 切り替える。"""
        if self._session:
            prev_app, start_ts, last_ts = self._session
            if prev_app == app_id and (now - last_ts) < config.SESSION_GAP:
                self._session = (app_id, start_ts, now)
                return
            self._close_session()
        self._session = (app_id, now, now)

    def _tick(self, delta: float) -> None:
        """1回分の観測。deltaは前回tickからの実経過秒数。"""
        if winapi.idle_seconds() >= config.IDLE_THRESHOLD:
            self._close_session()
            return

        current = winapi.foreground_app()
        if not current:
            return
        exe_path, _title = current
        if os.path.basename(exe_path).lower() in config.IGNORED_EXES:
            return

        app_id = self._resolve_app(exe_path)
        local = time.localtime()
        key = (time.strftime("%Y-%m-%d", local), local.tm_hour, app_id)
        self._buffer[key] += delta
        self._touch_session(app_id, time.time())

    # --- スレッド本体 ------------------------------------------------------
    def run(self) -> None:
        self._conn = db.connect(self._db_path)
        self._last_flush = time.monotonic()
        previous = time.monotonic()
        # 上限。スリープ復帰などで巨大なdeltaが入るのを防ぐ。
        max_delta = config.POLL_SECONDS * 3

        try:
            while not self._stop_event.is_set():
                self._stop_event.wait(config.POLL_SECONDS)
                if self._stop_event.is_set():
                    break

                now = time.monotonic()
                delta = now - previous
                previous = now
                if delta > max_delta:
                    # スリープ/休止からの復帰とみなし、この区間は計上せずセッションも切る。
                    self._close_session()
                    continue

                try:
                    self._tick(delta)
                except Exception as exc:  # 1回の失敗で計測全体を止めない
                    self.last_error = exc

                if now - self._last_flush >= config.FLUSH_SECONDS:
                    self._flush()
        finally:
            try:
                self._close_session()
                self._flush()
            except Exception as exc:
                self.last_error = exc
            if self._conn:
                self._conn.close()
                self._conn = None
