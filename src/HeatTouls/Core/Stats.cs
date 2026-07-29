using Microsoft.Data.Sqlite;

namespace HeatTouls.Core;

/// <summary>ランキング1行ぶんの集計。</summary>
public sealed record AppUsage(string Name, string ExeName, double Seconds, int Sessions);

/// <summary>ホームタブ1行ぶん。日別の内訳つき。</summary>
public sealed record AppDailyUsage(
    string Name,
    string ExeName,
    double Seconds,
    IReadOnlyDictionary<string, double> Daily);

/// <summary>概要タブが表示するアプリ1つの詳細。</summary>
public sealed record AppDetail(
    string Name,
    double TotalSeconds,
    int SessionCount,
    int ActiveDays,
    int CurrentStreak,
    int LongestStreak,
    int? PeakHour,
    IReadOnlyDictionary<string, double> Daily,
    IReadOnlyDictionary<int, double> Hourly,
    string? FirstDay,
    string? LastDay,
    double AverageSeconds,
    double Share);

/// <summary>
/// DBに溜まった稼働時間を、ダッシュボードが必要とする形に集計する。
/// すべてのメソッドは days(=直近何日か / null で全期間) を受け取る。
/// </summary>
public static class Stats
{
    public const string DayFmt = "yyyy-MM-dd";

    /// <summary>(cutoff日文字列 or null, cutoffのUnix秒) を返す。</summary>
    private static (string? Cutoff, double CutoffTs) Range(int? days)
    {
        if (days is null || days <= 0)
        {
            return (null, 0.0);
        }
        var start = Today().AddDays(-(days.Value - 1));
        return (start.ToString(DayFmt), LocalMidnightUnix(start));
    }

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>ローカル時刻でその日の 0:00 にあたる Unix 秒。</summary>
    public static double LocalMidnightUnix(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUnixTimeMilliseconds() / 1000.0;
    }

    private static SqliteCommand Command(Database db, string sql, params (string Name, object Value)[] args)
    {
        var cmd = db.Connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }

    private static double Secs(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? 0.0 : reader.GetDouble(index);

    // --- 全体の集計 ---------------------------------------------------------

    /// <summary>{'YYYY-MM-DD': 秒} の辞書。</summary>
    public static Dictionary<string, double> DailySeconds(Database db, int? days = null)
    {
        var (cutoff, _) = Range(days);
        using var cmd = cutoff is null
            ? Command(db, "SELECT day, SUM(seconds) FROM usage GROUP BY day")
            : Command(db, "SELECT day, SUM(seconds) FROM usage WHERE day >= $cut GROUP BY day",
                ("$cut", cutoff));

        var result = new Dictionary<string, double>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = Secs(reader, 1);
        }
        return result;
    }

    /// <summary>{0..23: 秒} の辞書(データのある時間帯のみ)。</summary>
    public static Dictionary<int, double> HourlySeconds(Database db, int? days = null)
    {
        var (cutoff, _) = Range(days);
        using var cmd = cutoff is null
            ? Command(db, "SELECT hour, SUM(seconds) FROM usage GROUP BY hour")
            : Command(db, "SELECT hour, SUM(seconds) FROM usage WHERE day >= $cut GROUP BY hour",
                ("$cut", cutoff));

        var result = new Dictionary<int, double>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetInt32(0)] = Secs(reader, 1);
        }
        return result;
    }

    /// <summary>アプリごとの合計とセッション数を、秒の降順で返す。</summary>
    public static List<AppUsage> AppBreakdown(Database db, int? days = null)
    {
        var (cutoff, cutoffTs) = Range(days);

        using var usageCmd = cutoff is null
            ? Command(db, """
                SELECT a.id, a.name, a.exe_name, SUM(u.seconds) AS s
                FROM usage u JOIN apps a ON a.id = u.app_id
                GROUP BY a.id ORDER BY s DESC
                """)
            : Command(db, """
                SELECT a.id, a.name, a.exe_name, SUM(u.seconds) AS s
                FROM usage u JOIN apps a ON a.id = u.app_id
                WHERE u.day >= $cut
                GROUP BY a.id ORDER BY s DESC
                """, ("$cut", cutoff));

        var rows = new List<(long Id, string Name, string ExeName, double Seconds)>();
        using (var reader = usageCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), Secs(reader, 3)));
            }
        }

        using var sessionCmd = cutoff is null
            ? Command(db, "SELECT app_id, COUNT(*) FROM sessions GROUP BY app_id")
            : Command(db, "SELECT app_id, COUNT(*) FROM sessions WHERE start_ts >= $ts GROUP BY app_id",
                ("$ts", cutoffTs));

        var sessions = new Dictionary<long, int>();
        using (var reader = sessionCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                sessions[reader.GetInt64(0)] = reader.GetInt32(1);
            }
        }

        return rows
            .Select(r => new AppUsage(r.Name, r.ExeName, r.Seconds,
                sessions.TryGetValue(r.Id, out var count) ? count : 0))
            .ToList();
    }

    /// <summary>アプリごとの日別稼働時間。ホームタブのヒートマップ一覧で使う。</summary>
    public static List<AppDailyUsage> AppDaily(Database db, int? days = null)
    {
        var (cutoff, _) = Range(days);
        const string query = """
            SELECT a.id, a.name, a.exe_name, u.day, SUM(u.seconds)
            FROM usage u JOIN apps a ON a.id = u.app_id
            {0}
            GROUP BY a.id, u.day
            """;

        using var cmd = cutoff is null
            ? Command(db, string.Format(query, ""))
            : Command(db, string.Format(query, "WHERE u.day >= $cut"), ("$cut", cutoff));

        var apps = new Dictionary<long, (string Name, string ExeName, double Total, Dictionary<string, double> Daily)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                if (!apps.TryGetValue(id, out var entry))
                {
                    entry = (reader.GetString(1), reader.GetString(2), 0.0, new Dictionary<string, double>());
                    apps[id] = entry;
                }
                var value = Secs(reader, 4);
                entry.Daily[reader.GetString(3)] = value;
                apps[id] = entry with { Total = entry.Total + value };
            }
        }

        return apps.Values
            .Select(a => new AppDailyUsage(a.Name, a.ExeName, a.Total, a.Daily))
            .OrderByDescending(a => a.Seconds)
            .ToList();
    }

    /// <summary>その期間に記録のあるアプリ名を、稼働時間の降順で返す。</summary>
    public static List<string> AppNames(Database db, int? days = null)
    {
        var (cutoff, _) = Range(days);
        const string query = """
            SELECT a.name, SUM(u.seconds) AS s FROM usage u
            JOIN apps a ON a.id = u.app_id
            {0}
            GROUP BY a.id ORDER BY s DESC
            """;

        using var cmd = cutoff is null
            ? Command(db, string.Format(query, ""))
            : Command(db, string.Format(query, "WHERE u.day >= $cut"), ("$cut", cutoff));

        var names = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    public static string? FirstRecordedDay(Database db)
    {
        using var cmd = Command(db, "SELECT MIN(day) FROM usage");
        var value = cmd.ExecuteScalar();
        return value is null or DBNull ? null : (string)value;
    }

    // --- アプリ1つに絞った集計 ------------------------------------------------

    private static Dictionary<string, double> DailyTotals(Database db, int? days, string? appName)
    {
        if (appName is null)
        {
            return DailySeconds(db, days);
        }

        var (cutoff, _) = Range(days);
        const string query = """
            SELECT u.day, SUM(u.seconds) FROM usage u
            JOIN apps a ON a.id = u.app_id
            WHERE a.name = $name {0}
            GROUP BY u.day
            """;

        using var cmd = cutoff is null
            ? Command(db, string.Format(query, ""), ("$name", appName))
            : Command(db, string.Format(query, "AND u.day >= $cut"), ("$name", appName), ("$cut", cutoff));

        var result = new Dictionary<string, double>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = Secs(reader, 1);
        }
        return result;
    }

    private static Dictionary<int, double> HourlyTotals(Database db, int? days, string? appName)
    {
        if (appName is null)
        {
            return HourlySeconds(db, days);
        }

        var (cutoff, _) = Range(days);
        const string query = """
            SELECT u.hour, SUM(u.seconds) FROM usage u
            JOIN apps a ON a.id = u.app_id
            WHERE a.name = $name {0}
            GROUP BY u.hour
            """;

        using var cmd = cutoff is null
            ? Command(db, string.Format(query, ""), ("$name", appName))
            : Command(db, string.Format(query, "AND u.day >= $cut"), ("$name", appName), ("$cut", cutoff));

        var result = new Dictionary<int, double>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetInt32(0)] = Secs(reader, 1);
        }
        return result;
    }

    /// <summary>(現在の連続日数, 最長連続日数) を返す。</summary>
    private static (int Current, int Longest) Streaks(IReadOnlyCollection<string> activeDays)
    {
        if (activeDays.Count == 0)
        {
            return (0, 0);
        }

        var days = activeDays
            .Select(d => DateOnly.ParseExact(d, DayFmt))
            .OrderBy(d => d)
            .ToList();
        var daySet = days.ToHashSet();

        var longest = 1;
        var run = 1;
        for (var i = 1; i < days.Count; i++)
        {
            run = days[i].DayNumber - days[i - 1].DayNumber == 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        // 現在の連続日数は、今日(まだ記録が無ければ昨日)を起点に遡って数える。
        var today = Today();
        var anchor = daySet.Contains(today) ? today : today.AddDays(-1);
        var current = 0;
        while (daySet.Contains(anchor))
        {
            current++;
            anchor = anchor.AddDays(-1);
        }
        return (current, longest);
    }

    /// <summary>指定アプリ1つの詳細。アプリ名が存在しなくても整合した空の値を返す。</summary>
    public static AppDetail Detail(Database db, string appName, int? days = null)
    {
        var empty = new AppDetail(
            appName, 0.0, 0, 0, 0, 0, null,
            new Dictionary<string, double>(), new Dictionary<int, double>(),
            null, null, 0.0, 0.0);

        long appId;
        using (var cmd = Command(db, "SELECT id FROM apps WHERE name = $name", ("$name", appName)))
        {
            var value = cmd.ExecuteScalar();
            if (value is null or DBNull)
            {
                return empty;
            }
            appId = Convert.ToInt64(value);
        }

        var (cutoff, cutoffTs) = Range(days);
        var daily = DailyTotals(db, days, appName);
        var hourly = HourlyTotals(db, days, appName);
        var total = daily.Values.Sum();

        int sessionCount;
        string? firstDay;
        string? lastDay;

        if (cutoff is null)
        {
            sessionCount = ScalarInt(db, "SELECT COUNT(*) FROM sessions WHERE app_id = $app",
                ("$app", appId));
            firstDay = ScalarString(db, "SELECT MIN(day) FROM usage WHERE app_id = $app",
                ("$app", appId));
            lastDay = ScalarString(db, "SELECT MAX(day) FROM usage WHERE app_id = $app",
                ("$app", appId));
        }
        else
        {
            sessionCount = ScalarInt(db,
                "SELECT COUNT(*) FROM sessions WHERE app_id = $app AND start_ts >= $ts",
                ("$app", appId), ("$ts", cutoffTs));
            firstDay = ScalarString(db, "SELECT MIN(day) FROM usage WHERE app_id = $app AND day >= $cut",
                ("$app", appId), ("$cut", cutoff));
            lastDay = ScalarString(db, "SELECT MAX(day) FROM usage WHERE app_id = $app AND day >= $cut",
                ("$app", appId), ("$cut", cutoff));
        }

        var active = daily.Where(kv => kv.Value >= Config.MinActiveDaySeconds)
            .Select(kv => kv.Key).ToList();
        var (currentStreak, longestStreak) = Streaks(active);
        int? peakHour = hourly.Count == 0
            ? null
            : hourly.Aggregate((a, b) => b.Value > a.Value ? b : a).Key;

        var overallTotal = DailySeconds(db, days).Values.Sum();

        return new AppDetail(
            appName,
            total,
            sessionCount,
            active.Count,
            currentStreak,
            longestStreak,
            peakHour,
            daily,
            hourly,
            firstDay,
            lastDay,
            active.Count > 0 ? total / active.Count : 0.0,
            overallTotal > 0 ? total / overallTotal : 0.0);
    }

    private static int ScalarInt(Database db, string sql, params (string, object)[] args)
    {
        using var cmd = Command(db, sql, args);
        var value = cmd.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static string? ScalarString(Database db, string sql, params (string, object)[] args)
    {
        using var cmd = Command(db, sql, args);
        var value = cmd.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value);
    }
}
