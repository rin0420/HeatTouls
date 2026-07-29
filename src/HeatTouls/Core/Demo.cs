namespace HeatTouls.Core;

/// <summary>UI確認用のサンプルデータ生成。本番DBとは別ファイルに書き込む前提。</summary>
public static class Demo
{
    private static readonly (string Path, string Name, double Weight)[] DemoApps =
    [
        (@"C:\Demo\Code.exe", "Visual Studio Code", 1.00),
        (@"C:\Demo\chrome.exe", "Google Chrome", 0.85),
        (@"C:\Demo\slack.exe", "Slack", 0.35),
        (@"C:\Demo\WindowsTerminal.exe", "Windows Terminal", 0.45),
        (@"C:\Demo\Discord.exe", "Discord", 0.30),
        (@"C:\Demo\Figma.exe", "Figma", 0.22),
        (@"C:\Demo\Notion.exe", "Notion", 0.18),
        (@"C:\Demo\steam.exe", "Steam", 0.15),
        (@"C:\Demo\EXCEL.EXE", "Microsoft Excel", 0.12),
        (@"C:\Demo\Spotify.exe", "Spotify", 0.10),
    ];

    /// <summary>時間帯ごとの活動しやすさ (0時〜23時)。</summary>
    private static readonly double[] HourWeights =
    [
        0.3, 0.1, 0.05, 0.0, 0.0, 0.0, 0.1, 0.6, 0.7, 0.9, 1.0, 0.9,
        0.5, 0.8, 0.9, 0.9, 0.8, 0.7, 0.6, 0.8, 1.0, 1.0, 0.9, 0.6,
    ];

    /// <summary>過去 days 日分のそれらしい稼働記録を書き込む。</summary>
    public static void Seed(Database db, int days = 150, int seed = 20260728)
    {
        var rng = new Random(seed);
        var apps = DemoApps.Select(a => (Id: db.AppId(a.Path, a.Name), a.Weight)).ToArray();

        var today = Stats.Today();
        var usageRows = new List<UsageRow>();

        for (var offset = days - 1; offset >= 0; offset--)
        {
            var day = today.AddDays(-offset);
            // 期間の後半ほど使用量が増え、週末は少し落ちるようにする。
            var momentum = 0.35 + 0.65 * (1 - (double)offset / days);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                momentum *= 0.3 + rng.NextDouble() * 0.5;
            }
            if (rng.NextDouble() < 0.12)   // 完全に触らない日
            {
                continue;
            }

            var dayStr = day.ToString(Stats.DayFmt);
            var midnight = Stats.LocalMidnightUnix(day);

            for (var hour = 0; hour < HourWeights.Length; hour++)
            {
                var hourWeight = HourWeights[hour];
                if (hourWeight <= 0 || rng.NextDouble() > hourWeight * momentum)
                {
                    continue;
                }

                var budget = 3600 * (0.35 + rng.NextDouble() * 0.65) * hourWeight;
                foreach (var (id, weight) in apps)
                {
                    if (rng.NextDouble() > weight * 0.55)
                    {
                        continue;
                    }
                    var seconds = budget * weight * (0.05 + rng.NextDouble() * 0.25);
                    if (seconds < 30)
                    {
                        continue;
                    }
                    usageRows.Add(new UsageRow(dayStr, hour, id, Math.Round(seconds, 1)));
                    var startTs = midnight + hour * 3600 + rng.NextDouble() * 1800;
                    db.AddSession(id, startTs, startTs + seconds, dayStr);
                }
            }
        }

        db.AddUsage(usageRows);
    }
}
