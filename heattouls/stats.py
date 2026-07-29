"""DBに溜まった稼働時間を、ダッシュボードが必要とする形に集計する。

すべての関数は days(=直近何日か / Noneで全期間) を受け取る。
"""

import datetime as dt
import time

from . import config

DAY_FMT = "%Y-%m-%d"


def _range(days):
    """(cutoff_day文字列 or None, cutoff_timestamp) を返す。"""
    if not days:
        return None, 0.0
    start = dt.date.today() - dt.timedelta(days=days - 1)
    cutoff_ts = time.mktime(start.timetuple())
    return start.strftime(DAY_FMT), cutoff_ts


def daily_seconds(conn, days=None) -> dict:
    """{'YYYY-MM-DD': 秒} の辞書。"""
    cutoff, _ = _range(days)
    if cutoff:
        rows = conn.execute(
            "SELECT day, SUM(seconds) FROM usage WHERE day >= ? GROUP BY day", (cutoff,)
        )
    else:
        rows = conn.execute("SELECT day, SUM(seconds) FROM usage GROUP BY day")
    return {day: float(secs or 0.0) for day, secs in rows}


def hourly_seconds(conn, days=None) -> dict:
    """{0..23: 秒} の辞書(データのある時間帯のみ)。"""
    cutoff, _ = _range(days)
    if cutoff:
        rows = conn.execute(
            "SELECT hour, SUM(seconds) FROM usage WHERE day >= ? GROUP BY hour", (cutoff,)
        )
    else:
        rows = conn.execute("SELECT hour, SUM(seconds) FROM usage GROUP BY hour")
    return {int(hour): float(secs or 0.0) for hour, secs in rows}


def app_breakdown(conn, days=None) -> list:
    """[{'name','exe_name','seconds','sessions'}, ...] を秒の降順で返す。"""
    cutoff, cutoff_ts = _range(days)
    if cutoff:
        usage_rows = conn.execute(
            """
            SELECT a.id, a.name, a.exe_name, SUM(u.seconds) AS s
            FROM usage u JOIN apps a ON a.id = u.app_id
            WHERE u.day >= ?
            GROUP BY a.id ORDER BY s DESC
            """,
            (cutoff,),
        ).fetchall()
        session_rows = conn.execute(
            "SELECT app_id, COUNT(*) FROM sessions WHERE start_ts >= ? GROUP BY app_id",
            (cutoff_ts,),
        ).fetchall()
    else:
        usage_rows = conn.execute(
            """
            SELECT a.id, a.name, a.exe_name, SUM(u.seconds) AS s
            FROM usage u JOIN apps a ON a.id = u.app_id
            GROUP BY a.id ORDER BY s DESC
            """
        ).fetchall()
        session_rows = conn.execute(
            "SELECT app_id, COUNT(*) FROM sessions GROUP BY app_id"
        ).fetchall()

    sessions = dict(session_rows)
    return [
        {
            "name": name,
            "exe_name": exe_name,
            "seconds": float(secs or 0.0),
            "sessions": int(sessions.get(app_id, 0)),
        }
        for app_id, name, exe_name, secs in usage_rows
    ]


def app_daily(conn, days=None) -> list:
    """アプリごとの日別稼働時間。ホームタブのヒートマップ一覧で使う。

    [{'name', 'exe_name', 'seconds', 'daily': {'YYYY-MM-DD': 秒}}, ...] を
    合計時間の降順で返す。
    """
    cutoff, _ = _range(days)
    query = """
        SELECT a.id, a.name, a.exe_name, u.day, SUM(u.seconds)
        FROM usage u JOIN apps a ON a.id = u.app_id
        {where}
        GROUP BY a.id, u.day
    """
    if cutoff:
        rows = conn.execute(query.format(where="WHERE u.day >= ?"), (cutoff,))
    else:
        rows = conn.execute(query.format(where=""))

    apps = {}
    for app_id, name, exe_name, day, seconds in rows:
        entry = apps.get(app_id)
        if entry is None:
            entry = apps[app_id] = {
                "name": name, "exe_name": exe_name, "seconds": 0.0, "daily": {},
            }
        value = float(seconds or 0.0)
        entry["daily"][day] = value
        entry["seconds"] += value

    return sorted(apps.values(), key=lambda a: a["seconds"], reverse=True)


def _streaks(active_days: set):
    """(現在の連続日数, 最長連続日数) を返す。"""
    if not active_days:
        return 0, 0
    days = sorted(dt.datetime.strptime(d, DAY_FMT).date() for d in active_days)
    day_set = set(days)

    longest = run = 1
    for prev, cur in zip(days, days[1:]):
        run = run + 1 if (cur - prev).days == 1 else 1
        longest = max(longest, run)

    # 現在の連続日数は、今日(まだ記録が無ければ昨日)を起点に遡って数える。
    today = dt.date.today()
    anchor = today if today in day_set else today - dt.timedelta(days=1)
    current = 0
    while anchor in day_set:
        current += 1
        anchor -= dt.timedelta(days=1)
    return current, longest


def overview(conn, days=None) -> dict:
    """ダッシュボード上段のカードに出す値をまとめて返す。"""
    cutoff, cutoff_ts = _range(days)
    daily = daily_seconds(conn, days)
    hourly = hourly_seconds(conn, days)
    apps = app_breakdown(conn, days)

    total = sum(daily.values())
    if cutoff:
        session_count = conn.execute(
            "SELECT COUNT(*) FROM sessions WHERE start_ts >= ?", (cutoff_ts,)
        ).fetchone()[0]
    else:
        session_count = conn.execute("SELECT COUNT(*) FROM sessions").fetchone()[0]

    active = {d for d, s in daily.items() if s >= config.MIN_ACTIVE_DAY_SECONDS}
    current_streak, longest_streak = _streaks(active)
    peak_hour = max(hourly, key=hourly.get) if hourly else None

    return {
        "total_seconds": total,
        "session_count": int(session_count),
        "app_count": len(apps),
        "active_days": len(active),
        "current_streak": current_streak,
        "longest_streak": longest_streak,
        "peak_hour": peak_hour,
        "top_app": apps[0]["name"] if apps else None,
        "top_app_seconds": apps[0]["seconds"] if apps else 0.0,
        "daily": daily,
        "hourly": hourly,
        "apps": apps,
    }


def first_recorded_day(conn):
    row = conn.execute("SELECT MIN(day) FROM usage").fetchone()
    return row[0] if row and row[0] else None


WEEKDAY_JA = ("月", "火", "水", "木", "金", "土", "日")


def _daily_totals(conn, days=None, app_name=None) -> dict:
    """{'YYYY-MM-DD': 秒}。app_name指定時はそのアプリだけに絞る。"""
    if app_name is None:
        return daily_seconds(conn, days)
    cutoff, _ = _range(days)
    query = """
        SELECT u.day, SUM(u.seconds) FROM usage u
        JOIN apps a ON a.id = u.app_id
        WHERE a.name = ? {and_cutoff}
        GROUP BY u.day
    """
    if cutoff:
        rows = conn.execute(query.format(and_cutoff="AND u.day >= ?"), (app_name, cutoff))
    else:
        rows = conn.execute(query.format(and_cutoff=""), (app_name,))
    return {day: float(secs or 0.0) for day, secs in rows}


def _hourly_totals(conn, days=None, app_name=None) -> dict:
    """{0..23: 秒}。app_name指定時はそのアプリだけに絞る。"""
    if app_name is None:
        return hourly_seconds(conn, days)
    cutoff, _ = _range(days)
    query = """
        SELECT u.hour, SUM(u.seconds) FROM usage u
        JOIN apps a ON a.id = u.app_id
        WHERE a.name = ? {and_cutoff}
        GROUP BY u.hour
    """
    if cutoff:
        rows = conn.execute(query.format(and_cutoff="AND u.day >= ?"), (app_name, cutoff))
    else:
        rows = conn.execute(query.format(and_cutoff=""), (app_name,))
    return {int(hour): float(secs or 0.0) for hour, secs in rows}


def _first_day(conn, app_name=None):
    """記録の最初の日('YYYY-MM-DD')。app_name指定時はそのアプリの最初の日。"""
    if app_name is None:
        return first_recorded_day(conn)
    row = conn.execute(
        "SELECT MIN(u.day) FROM usage u JOIN apps a ON a.id = u.app_id WHERE a.name = ?",
        (app_name,),
    ).fetchone()
    return row[0] if row and row[0] else None


def _sum_seconds(conn, start_day, end_day, app_name=None) -> float:
    """[start_day, end_day] (両端含む) の合計秒数。"""
    if app_name is None:
        row = conn.execute(
            "SELECT SUM(seconds) FROM usage WHERE day >= ? AND day <= ?",
            (start_day, end_day),
        ).fetchone()
    else:
        row = conn.execute(
            """
            SELECT SUM(u.seconds) FROM usage u
            JOIN apps a ON a.id = u.app_id
            WHERE a.name = ? AND u.day >= ? AND u.day <= ?
            """,
            (app_name, start_day, end_day),
        ).fetchone()
    return float(row[0] or 0.0)


def _period_bounds(days, previous=False):
    """(start_day, end_day) 文字列を返す。previous=Trueなら「前の同期間」。"""
    today = dt.date.today()
    if previous:
        end = today - dt.timedelta(days=days + 1)
        start = today - dt.timedelta(days=2 * days)
    else:
        start = today - dt.timedelta(days=days - 1)
        end = today
    return start.strftime(DAY_FMT), end.strftime(DAY_FMT)


def buckets(conn, days=None, app_name=None) -> list:
    """棒グラフ用の時系列バケットを古い順で返す(データの無い日/週も0で埋める)。

    days=7   : 直近7日を1日ごと({'label': 曜日1文字, 'sub': '月/日'})
    days=30  : 直近30日を1日ごと({'label': '日', 'sub': '月/日'})
    days=None: 全期間を週ごと(月曜始まり、最大52週)

    各要素は {'label': str, 'seconds': float, 'sub': str}。app_name指定時は
    そのアプリだけに絞る。
    """
    if days == 7 or days == 30:
        totals = _daily_totals(conn, days, app_name)
        today = dt.date.today()
        result = []
        for offset in range(days - 1, -1, -1):
            day = today - dt.timedelta(days=offset)
            label = WEEKDAY_JA[day.weekday()] if days == 7 else str(day.day)
            result.append({
                "label": label,
                "seconds": totals.get(day.strftime(DAY_FMT), 0.0),
                "sub": f"{day.month}/{day.day}",
            })
        return result

    # days=None: 全期間を週ごと(月曜始まり)に集計する。
    first_day = _first_day(conn, app_name)
    if not first_day:
        return []
    totals = _daily_totals(conn, None, app_name)
    first = dt.datetime.strptime(first_day, DAY_FMT).date()
    first_monday = first - dt.timedelta(days=first.weekday())
    today = dt.date.today()
    this_monday = today - dt.timedelta(days=today.weekday())

    week_starts = []
    monday = first_monday
    while monday <= this_monday:
        week_starts.append(monday)
        monday += dt.timedelta(days=7)
    if len(week_starts) > 52:
        week_starts = week_starts[-52:]

    result = []
    for week_start in week_starts:
        week_end = week_start + dt.timedelta(days=6)
        total = 0.0
        day = week_start
        while day <= week_end:
            total += totals.get(day.strftime(DAY_FMT), 0.0)
            day += dt.timedelta(days=1)
        result.append({
            "label": f"{week_start.month}/{week_start.day}",
            "seconds": total,
            "sub": f"{week_start.month}/{week_start.day}〜{week_end.month}/{week_end.day}",
        })
    return result


def previous_total(conn, days):
    """「前の同期間」の合計秒数。days=Noneなら比較対象が無いのでNone。"""
    if not days:
        return None
    start_day, end_day = _period_bounds(days, previous=True)
    return _sum_seconds(conn, start_day, end_day)


def delta_ratio(conn, days, app_name=None):
    """現在の期間と前の同期間の増減率 (現在-前)/前。前が0またはdays=NoneならNone。"""
    if not days:
        return None
    cur_start, cur_end = _period_bounds(days, previous=False)
    prev_start, prev_end = _period_bounds(days, previous=True)
    current = _sum_seconds(conn, cur_start, cur_end, app_name)
    previous = _sum_seconds(conn, prev_start, prev_end, app_name)
    if not previous:
        return None
    return (current - previous) / previous


def app_names(conn, days=None) -> list:
    """その期間に記録のあるアプリ名を、稼働時間の降順で返す。"""
    cutoff, _ = _range(days)
    query = """
        SELECT a.name, SUM(u.seconds) AS s FROM usage u
        JOIN apps a ON a.id = u.app_id
        {where}
        GROUP BY a.id ORDER BY s DESC
    """
    if cutoff:
        rows = conn.execute(query.format(where="WHERE u.day >= ?"), (cutoff,))
    else:
        rows = conn.execute(query.format(where=""))
    return [name for name, _ in rows]


def app_detail(conn, app_name, days=None) -> dict:
    """指定アプリ1つの詳細。アプリ名が存在しなくても整合した空の辞書を返す。"""
    empty = {
        "name": app_name,
        "total_seconds": 0.0,
        "session_count": 0,
        "active_days": 0,
        "current_streak": 0,
        "longest_streak": 0,
        "peak_hour": None,
        "daily": {},
        "hourly": {},
        "first_day": None,
        "last_day": None,
        "average_seconds": 0.0,
        "share": 0.0,
    }

    row = conn.execute("SELECT id FROM apps WHERE name = ?", (app_name,)).fetchone()
    if not row:
        return empty
    app_id_ = row[0]

    cutoff, cutoff_ts = _range(days)
    daily = _daily_totals(conn, days, app_name)
    hourly = _hourly_totals(conn, days, app_name)
    total = sum(daily.values())

    if cutoff:
        session_count = conn.execute(
            "SELECT COUNT(*) FROM sessions WHERE app_id = ? AND start_ts >= ?",
            (app_id_, cutoff_ts),
        ).fetchone()[0]
        first_row = conn.execute(
            "SELECT MIN(day) FROM usage WHERE app_id = ? AND day >= ?", (app_id_, cutoff)
        ).fetchone()
        last_row = conn.execute(
            "SELECT MAX(day) FROM usage WHERE app_id = ? AND day >= ?", (app_id_, cutoff)
        ).fetchone()
    else:
        session_count = conn.execute(
            "SELECT COUNT(*) FROM sessions WHERE app_id = ?", (app_id_,)
        ).fetchone()[0]
        first_row = conn.execute("SELECT MIN(day) FROM usage WHERE app_id = ?", (app_id_,)).fetchone()
        last_row = conn.execute("SELECT MAX(day) FROM usage WHERE app_id = ?", (app_id_,)).fetchone()

    active = {d for d, s in daily.items() if s >= config.MIN_ACTIVE_DAY_SECONDS}
    current_streak, longest_streak = _streaks(active)
    peak_hour = max(hourly, key=hourly.get) if hourly else None

    overall_total = sum(daily_seconds(conn, days).values())

    return {
        "name": app_name,
        "total_seconds": total,
        "session_count": int(session_count),
        "active_days": len(active),
        "current_streak": current_streak,
        "longest_streak": longest_streak,
        "peak_hour": peak_hour,
        "daily": daily,
        "hourly": hourly,
        "first_day": first_row[0] if first_row else None,
        "last_day": last_row[0] if last_row else None,
        "average_seconds": (total / len(active)) if active else 0.0,
        "share": (total / overall_total) if overall_total else 0.0,
    }
