namespace HeatTouls.Core;

/// <summary>数値・時間の表示整形。</summary>
public static class Fmt
{
    /// <summary>カードに出す短い表記。例: "45分" / "12.4時間" / "1,234時間"</summary>
    public static string Duration(double seconds)
    {
        if (seconds < 60)
        {
            return $"{(int)seconds}秒";
        }
        var minutes = seconds / 60;
        if (minutes < 60)
        {
            return $"{(int)minutes}分";
        }
        var hours = seconds / 3600;
        return hours < 100 ? $"{hours:F1}時間" : $"{hours:N0}時間";
    }

    /// <summary>ツールチップ等で使う詳しい表記。例: "3時間12分"</summary>
    public static string DurationLong(double seconds)
    {
        var total = (int)seconds;
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        if (hours > 0 && minutes > 0)
        {
            return $"{hours}時間{minutes}分";
        }
        if (hours > 0)
        {
            return $"{hours}時間";
        }
        return minutes > 0 ? $"{minutes}分" : $"{total}秒";
    }

    public static string Count(int value) => value.ToString("N0");

    public static string HourLabel(int? hour) => hour is null ? "—" : $"{hour}時";

    public static string Days(int value) => $"{value}日";

    public static string Truncate(string text, int limit = 26) =>
        text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit - 1), "…");
}
