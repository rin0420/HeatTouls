"""数値・時間の表示整形。"""


def duration(seconds: float) -> str:
    """カードに出す短い表記。例: '45分' / '12.4時間' / '1,234時間'"""
    if seconds < 60:
        return f"{int(seconds)}秒"
    minutes = seconds / 60
    if minutes < 60:
        return f"{int(minutes)}分"
    hours = seconds / 3600
    if hours < 100:
        return f"{hours:.1f}時間"
    return f"{hours:,.0f}時間"


def duration_long(seconds: float) -> str:
    """ツールチップ等で使う詳しい表記。例: '3時間12分'"""
    total = int(seconds)
    hours, rem = divmod(total, 3600)
    minutes = rem // 60
    if hours and minutes:
        return f"{hours}時間{minutes}分"
    if hours:
        return f"{hours}時間"
    if minutes:
        return f"{minutes}分"
    return f"{total}秒"


def count(value: int) -> str:
    return f"{value:,}"


def hour_label(hour) -> str:
    return "—" if hour is None else f"{int(hour)}時"


def days(value: int) -> str:
    return f"{value}日"


def truncate(text: str, limit: int = 26) -> str:
    return text if len(text) <= limit else text[: limit - 1] + "…"
