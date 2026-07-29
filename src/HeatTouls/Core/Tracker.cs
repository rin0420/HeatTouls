using System.Diagnostics;

namespace HeatTouls.Core;

/// <summary>
/// 前面ウィンドウを一定間隔で観測し、アクティブ時間を積算するトラッカー。
///
/// 1秒ごとに「今どのアプリが前面か」「無操作は何秒続いているか」を見て、
/// アイドルでなければ経過時間をそのアプリの (日, 時) バケットへ足し込む。
/// バケットは一定間隔でまとめてDBへ書き出す。
/// </summary>
public sealed class Tracker : IDisposable
{
    private readonly string? _dbPath;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<(string Day, int Hour, long AppId), double> _buffer = new();
    private readonly Dictionary<string, long> _appCache = new(StringComparer.OrdinalIgnoreCase);

    private Thread? _thread;
    private Database? _db;
    private (long AppId, double StartTs, double LastTs)? _session;
    private double _lastFlush;

    /// <summary>直近に握りつぶした例外。1回の失敗で計測を止めないための記録。</summary>
    public Exception? LastError { get; private set; }

    public Tracker(string? dbPath = null) => _dbPath = dbPath;

    public bool IsRunning => _thread?.IsAlive ?? false;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }
        _thread = new Thread(Run)
        {
            Name = "heattouls-tracker",
            IsBackground = true,
        };
        _thread.Start();
    }

    public void Stop() => _cts.Cancel();

    public bool Join(TimeSpan timeout) => _thread is null || _thread.Join(timeout);

    // --- 内部 --------------------------------------------------------------

    private static double Monotonic() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private static double UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    private long ResolveApp(string exePath)
    {
        if (_appCache.TryGetValue(exePath, out var cached))
        {
            return cached;
        }
        var name = WinApi.FileDescription(exePath) ?? Path.GetFileNameWithoutExtension(exePath);
        var id = _db!.AppId(exePath, name);
        _appCache[exePath] = id;
        return id;
    }

    private void Flush()
    {
        if (_buffer.Count > 0)
        {
            var rows = _buffer
                .Select(kv => new UsageRow(kv.Key.Day, kv.Key.Hour, kv.Key.AppId, kv.Value))
                .ToList();
            _db!.AddUsage(rows);
            _buffer.Clear();
        }
        _lastFlush = Monotonic();
    }

    private void CloseSession()
    {
        if (_session is not { } session)
        {
            return;
        }
        _session = null;

        var duration = session.LastTs - session.StartTs;
        if (duration >= Config.MinSessionSeconds)
        {
            var day = DateTimeOffset.FromUnixTimeMilliseconds((long)(session.StartTs * 1000))
                .ToLocalTime().ToString(Stats.DayFmt);
            _db!.AddSession(session.AppId, session.StartTs, session.LastTs, day);
        }
    }

    /// <summary>セッションを継続 or 切り替える。</summary>
    private void TouchSession(long appId, double now)
    {
        if (_session is { } session)
        {
            if (session.AppId == appId && now - session.LastTs < Config.SessionGap)
            {
                _session = (appId, session.StartTs, now);
                return;
            }
            CloseSession();
        }
        _session = (appId, now, now);
    }

    /// <summary>1回分の観測。delta は前回 tick からの実経過秒数。</summary>
    private void Tick(double delta)
    {
        if (WinApi.IdleSeconds() >= Config.IdleThreshold)
        {
            CloseSession();
            return;
        }

        var current = WinApi.ForegroundApp();
        if (current is not { } app)
        {
            return;
        }
        if (Config.IgnoredExes.Contains(Path.GetFileName(app.ExePath)))
        {
            return;
        }

        var appId = ResolveApp(app.ExePath);
        var local = DateTime.Now;
        var key = (local.ToString(Stats.DayFmt), local.Hour, appId);
        _buffer[key] = _buffer.TryGetValue(key, out var acc) ? acc + delta : delta;
        TouchSession(appId, UnixNow());
    }

    // --- スレッド本体 --------------------------------------------------------

    private void Run()
    {
        var token = _cts.Token;
        try
        {
            _db = Database.Open(_dbPath);
        }
        catch (Exception exc)
        {
            LastError = exc;
            return;
        }

        _lastFlush = Monotonic();
        var previous = Monotonic();
        // 上限。スリープ復帰などで巨大な delta が入るのを防ぐ。
        var maxDelta = Config.PollSeconds * 3;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(Config.PollSeconds)))
                {
                    break;
                }

                var now = Monotonic();
                var delta = now - previous;
                previous = now;
                if (delta > maxDelta)
                {
                    // スリープ/休止からの復帰とみなし、この区間は計上せずセッションも切る。
                    CloseSession();
                    continue;
                }

                try
                {
                    Tick(delta);
                }
                catch (Exception exc)   // 1回の失敗で計測全体を止めない
                {
                    LastError = exc;
                }

                if (now - _lastFlush >= Config.FlushSeconds)
                {
                    Flush();
                }
            }
        }
        finally
        {
            try
            {
                CloseSession();
                Flush();
            }
            catch (Exception exc)
            {
                LastError = exc;
            }
            _db?.Dispose();
            _db = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
