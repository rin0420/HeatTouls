"""SQLiteスキーマと書き込みヘルパ。

テーブル:
    apps     : 実行ファイルごとのマスタ (表示名つき)
    usage    : (日, 時, アプリ) 単位のアクティブ秒数
    sessions : 連続して使っていた区間
"""

import os
import sqlite3
from pathlib import Path

from . import config

SCHEMA = """
CREATE TABLE IF NOT EXISTS apps (
    id       INTEGER PRIMARY KEY,
    exe_path TEXT NOT NULL UNIQUE,
    exe_name TEXT NOT NULL,
    name     TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS usage (
    day     TEXT    NOT NULL,
    hour    INTEGER NOT NULL,
    app_id  INTEGER NOT NULL REFERENCES apps(id),
    seconds REAL    NOT NULL DEFAULT 0,
    PRIMARY KEY (day, hour, app_id)
);

CREATE INDEX IF NOT EXISTS idx_usage_day ON usage(day);

CREATE TABLE IF NOT EXISTS sessions (
    id       INTEGER PRIMARY KEY,
    app_id   INTEGER NOT NULL REFERENCES apps(id),
    start_ts REAL NOT NULL,
    end_ts   REAL NOT NULL,
    day      TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_sessions_day ON sessions(day);
"""


def connect(path: Path | str | None = None) -> sqlite3.Connection:
    """DBへ接続し、スキーマを用意して返す。"""
    target = Path(path) if path else config.db_path()
    target.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(str(target), timeout=10.0)
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA synchronous=NORMAL")
    conn.executescript(SCHEMA)
    conn.commit()
    return conn


def app_id(conn: sqlite3.Connection, exe_path: str, display_name: str | None = None) -> int:
    """実行ファイルパスに対応するapps.idを取得する(無ければ作る)。"""
    row = conn.execute("SELECT id FROM apps WHERE exe_path = ?", (exe_path,)).fetchone()
    if row:
        return row[0]
    exe_name = os.path.basename(exe_path)
    name = display_name or os.path.splitext(exe_name)[0]
    cur = conn.execute(
        "INSERT INTO apps (exe_path, exe_name, name) VALUES (?, ?, ?)",
        (exe_path, exe_name, name),
    )
    conn.commit()
    return cur.lastrowid


def add_usage(conn: sqlite3.Connection, rows) -> None:
    """[(day, hour, app_id, seconds), ...] を積み上げ加算する。"""
    if not rows:
        return
    conn.executemany(
        """
        INSERT INTO usage (day, hour, app_id, seconds) VALUES (?, ?, ?, ?)
        ON CONFLICT(day, hour, app_id) DO UPDATE SET seconds = seconds + excluded.seconds
        """,
        rows,
    )
    conn.commit()


def add_session(conn: sqlite3.Connection, app_id_: int, start_ts: float, end_ts: float, day: str) -> None:
    conn.execute(
        "INSERT INTO sessions (app_id, start_ts, end_ts, day) VALUES (?, ?, ?, ?)",
        (app_id_, start_ts, end_ts, day),
    )
    conn.commit()


def is_empty(conn: sqlite3.Connection) -> bool:
    return conn.execute("SELECT COUNT(*) FROM usage").fetchone()[0] == 0
