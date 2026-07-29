"""UI確認用のサンプルデータ生成。本番DBとは別ファイルに書き込む前提。"""

import datetime as dt
import random
import time

from . import db

DEMO_APPS = [
    (r"C:\Demo\Code.exe", "Visual Studio Code", 1.00),
    (r"C:\Demo\chrome.exe", "Google Chrome", 0.85),
    (r"C:\Demo\slack.exe", "Slack", 0.35),
    (r"C:\Demo\WindowsTerminal.exe", "Windows Terminal", 0.45),
    (r"C:\Demo\Discord.exe", "Discord", 0.30),
    (r"C:\Demo\Figma.exe", "Figma", 0.22),
    (r"C:\Demo\Notion.exe", "Notion", 0.18),
    (r"C:\Demo\steam.exe", "Steam", 0.15),
    (r"C:\Demo\EXCEL.EXE", "Microsoft Excel", 0.12),
    (r"C:\Demo\Spotify.exe", "Spotify", 0.10),
]

# 時間帯ごとの活動しやすさ (0時〜23時)
HOUR_WEIGHTS = [
    0.3, 0.1, 0.05, 0.0, 0.0, 0.0, 0.1, 0.6, 0.7, 0.9, 1.0, 0.9,
    0.5, 0.8, 0.9, 0.9, 0.8, 0.7, 0.6, 0.8, 1.0, 1.0, 0.9, 0.6,
]


def seed(conn, days: int = 150, rng_seed: int = 20260728) -> None:
    """過去days日分のそれらしい稼働記録を書き込む。"""
    rng = random.Random(rng_seed)
    app_ids = [(db.app_id(conn, path, name), weight) for path, name, weight in DEMO_APPS]

    today = dt.date.today()
    usage_rows = []
    for offset in range(days - 1, -1, -1):
        day = today - dt.timedelta(days=offset)
        # 期間の後半ほど使用量が増え、週末は少し落ちるようにする。
        momentum = 0.35 + 0.65 * (1 - offset / days)
        if day.weekday() >= 5:
            momentum *= rng.uniform(0.3, 0.8)
        if rng.random() < 0.12:  # 完全に触らない日
            continue

        day_str = day.strftime("%Y-%m-%d")
        for hour, hour_weight in enumerate(HOUR_WEIGHTS):
            if hour_weight <= 0 or rng.random() > hour_weight * momentum:
                continue
            budget = 3600 * rng.uniform(0.35, 1.0) * hour_weight
            for app_id_, weight in app_ids:
                if rng.random() > weight * 0.55:
                    continue
                seconds = budget * weight * rng.uniform(0.05, 0.30)
                if seconds < 30:
                    continue
                usage_rows.append((day_str, hour, app_id_, round(seconds, 1)))
                start_ts = time.mktime(day.timetuple()) + hour * 3600 + rng.uniform(0, 1800)
                db.add_session(conn, app_id_, start_ts, start_ts + seconds, day_str)

    db.add_usage(conn, usage_rows)
