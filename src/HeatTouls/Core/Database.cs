using Microsoft.Data.Sqlite;

namespace HeatTouls.Core;

/// <summary>(日, 時, アプリ) 単位の積み上げ1件。</summary>
public readonly record struct UsageRow(string Day, int Hour, long AppId, double Seconds);

/// <summary>
/// SQLite スキーマと書き込みヘルパ。Python 版と同じスキーマなので、
/// 既存の usage.db をそのまま読み書きできる。
///
/// テーブル:
///   apps     : 実行ファイルごとのマスタ (表示名つき)
///   usage    : (日, 時, アプリ) 単位のアクティブ秒数
///   sessions : 連続して使っていた区間
/// </summary>
public sealed class Database : IDisposable
{
    private const string Schema = """
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
        """;

    private readonly SqliteConnection _conn;

    private Database(SqliteConnection conn) => _conn = conn;

    public SqliteConnection Connection => _conn;

    /// <summary>DBへ接続し、スキーマを用意して返す。</summary>
    public static Database Open(string? path = null)
    {
        var target = path ?? Config.DbPath();
        var parent = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = target,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 10,
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();

        // 計測スレッドとUIが別接続で同じファイルを触るので、WALで読み書きを並行させる。
        Execute(conn, "PRAGMA journal_mode=WAL");
        Execute(conn, "PRAGMA synchronous=NORMAL");
        Execute(conn, "PRAGMA busy_timeout=10000");

        // 20秒ごとの書き出しは毎回ほぼ同じページを書き換えるので、WALは中身の
        // 割に膨らみやすい。既定のまま(1000ページ)だと、本体が100KBに満たない
        // うちからWALだけ4MBに達する。しかも既定ではチェックポイント後に巻き戻す
        // だけでファイルを切り詰めないため、一度伸びた4MBが居座り続ける。
        // 早めにチェックポイントし、そのあと切り詰めさせる。
        Execute(conn, "PRAGMA wal_autocheckpoint=200");
        Execute(conn, "PRAGMA journal_size_limit=524288");
        Execute(conn, Schema);
        return new Database(conn);
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>実行ファイルパスに対応する apps.id を取得する(無ければ作る)。</summary>
    public long AppId(string exePath, string? displayName = null)
    {
        using (var select = _conn.CreateCommand())
        {
            select.CommandText = "SELECT id FROM apps WHERE exe_path = $path";
            select.Parameters.AddWithValue("$path", exePath);
            var existing = select.ExecuteScalar();
            if (existing is not null and not DBNull)
            {
                return Convert.ToInt64(existing);
            }
        }

        var exeName = Path.GetFileName(exePath);
        var name = string.IsNullOrEmpty(displayName)
            ? Path.GetFileNameWithoutExtension(exeName)
            : displayName;

        using var insert = _conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO apps (exe_path, exe_name, name) VALUES ($path, $exe, $name);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$path", exePath);
        insert.Parameters.AddWithValue("$exe", exeName);
        insert.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(insert.ExecuteScalar());
    }

    /// <summary>(day, hour, app_id, seconds) を積み上げ加算する。</summary>
    public void AddUsage(IReadOnlyCollection<UsageRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO usage (day, hour, app_id, seconds) VALUES ($day, $hour, $app, $secs)
            ON CONFLICT(day, hour, app_id) DO UPDATE SET seconds = seconds + excluded.seconds
            """;
        var day = cmd.Parameters.Add("$day", SqliteType.Text);
        var hour = cmd.Parameters.Add("$hour", SqliteType.Integer);
        var app = cmd.Parameters.Add("$app", SqliteType.Integer);
        var secs = cmd.Parameters.Add("$secs", SqliteType.Real);

        foreach (var row in rows)
        {
            day.Value = row.Day;
            hour.Value = row.Hour;
            app.Value = row.AppId;
            secs.Value = row.Seconds;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void AddSession(long appId, double startTs, double endTs, string day)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO sessions (app_id, start_ts, end_ts, day) VALUES ($app, $start, $end, $day)";
        cmd.Parameters.AddWithValue("$app", appId);
        cmd.Parameters.AddWithValue("$start", startTs);
        cmd.Parameters.AddWithValue("$end", endTs);
        cmd.Parameters.AddWithValue("$day", day);
        cmd.ExecuteNonQuery();
    }

    public bool IsEmpty()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM usage";
        return Convert.ToInt64(cmd.ExecuteScalar()) == 0;
    }

    public void Dispose() => _conn.Dispose();
}
