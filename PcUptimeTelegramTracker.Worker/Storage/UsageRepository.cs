using Microsoft.Data.Sqlite;

namespace PcUptimeTelegramTracker.Worker.Storage;

// Persists per-session app CPU usage and session summaries in a local SQLite
// file. No external DB server needed — SQLite is just a single file on disk.
public class UsageRepository
{
    private readonly string _connectionString;

    public UsageRepository()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PcUptimeTelegramTracker");
        Directory.CreateDirectory(directory);
        var dbPath = Path.Combine(directory, "usage.db");
        _connectionString = $"Data Source={dbPath}";

        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS Sessions (
                                  Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                  StartTime TEXT NOT NULL,
                                  EndTime TEXT NOT NULL,
                                  AwakeSeconds INTEGER NOT NULL,
                                  SleepSeconds INTEGER NOT NULL,
                                  EndedCleanly INTEGER NOT NULL
                              );

                              CREATE TABLE IF NOT EXISTS AppUsage (
                                  SessionStartTime TEXT NOT NULL,
                                  ProcessName TEXT NOT NULL,
                                  CpuTimeTicks INTEGER NOT NULL,
                                  PRIMARY KEY (SessionStartTime, ProcessName)
                              );

                              CREATE TABLE IF NOT EXISTS SessionSampleCounts (
                                  SessionStartTime TEXT PRIMARY KEY,
                                  SampleCount INTEGER NOT NULL
                              );
                              """;
        command.ExecuteNonQuery();
    }

    // Writes an entire sampling round (all process deltas + the sample count
    // increment) in a single connection + transaction — far cheaper than
    // opening/closing a connection per process, per minute.
    public void AddAppUsageBatch(DateTime sessionStartTime, IReadOnlyList<(string ProcessName, TimeSpan Delta)> deltas)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using var upsertAppCommand = connection.CreateCommand();
        upsertAppCommand.Transaction = transaction;
        upsertAppCommand.CommandText = """
            INSERT INTO AppUsage (SessionStartTime, ProcessName, CpuTimeTicks)
            VALUES ($sessionStart, $processName, $ticks)
            ON CONFLICT(SessionStartTime, ProcessName)
            DO UPDATE SET CpuTimeTicks = CpuTimeTicks + excluded.CpuTimeTicks;
            """;
        var sessionStartParam = upsertAppCommand.Parameters.Add("$sessionStart", SqliteType.Text);
        var processNameParam = upsertAppCommand.Parameters.Add("$processName", SqliteType.Text);
        var ticksParam = upsertAppCommand.Parameters.Add("$ticks", SqliteType.Integer);

        var sessionStartText = sessionStartTime.ToString("o");
        foreach (var (processName, delta) in deltas)
        {
            sessionStartParam.Value = sessionStartText;
            processNameParam.Value = processName;
            ticksParam.Value = delta.Ticks;
            upsertAppCommand.ExecuteNonQuery();
        }

        using var sampleCountCommand = connection.CreateCommand();
        sampleCountCommand.Transaction = transaction;
        sampleCountCommand.CommandText = """
            INSERT INTO SessionSampleCounts (SessionStartTime, SampleCount)
            VALUES ($sessionStart, 1)
            ON CONFLICT(SessionStartTime) DO UPDATE SET SampleCount = SampleCount + 1;
            """;
        sampleCountCommand.Parameters.AddWithValue("$sessionStart", sessionStartText);
        sampleCountCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    public int GetSampleCount(DateTime sessionStartTime)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SampleCount FROM SessionSampleCounts WHERE SessionStartTime = $sessionStart;";
        command.Parameters.AddWithValue("$sessionStart", sessionStartTime.ToString("o"));

        var result = command.ExecuteScalar();
        return result is long count ? (int)count : 0;
    }

    public List<(string ProcessName, TimeSpan CpuTime)> GetTopApps(DateTime sessionStartTime, int count)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ProcessName, CpuTimeTicks
            FROM AppUsage
            WHERE SessionStartTime = $sessionStart
            ORDER BY CpuTimeTicks DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$sessionStart", sessionStartTime.ToString("o"));
        command.Parameters.AddWithValue("$count", count);

        var results = new List<(string, TimeSpan)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((reader.GetString(0), TimeSpan.FromTicks(reader.GetInt64(1))));
        }
        return results;
    }

    public void SaveSession(DateTime startTime, DateTime endTime, TimeSpan awake, TimeSpan sleep, bool endedCleanly)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Sessions (StartTime, EndTime, AwakeSeconds, SleepSeconds, EndedCleanly)
            VALUES ($start, $end, $awake, $sleep, $clean);
            """;
        command.Parameters.AddWithValue("$start", startTime.ToString("o"));
        command.Parameters.AddWithValue("$end", endTime.ToString("o"));
        command.Parameters.AddWithValue("$awake", (long)awake.TotalSeconds);
        command.Parameters.AddWithValue("$sleep", (long)sleep.TotalSeconds);
        command.Parameters.AddWithValue("$clean", endedCleanly ? 1 : 0);
        command.ExecuteNonQuery();
    }

    // Keeps the database small — deletes anything older than the cutoff (1 week).
    public void PruneOlderThan(DateTime cutoff)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM AppUsage WHERE SessionStartTime < $cutoff;
            DELETE FROM Sessions WHERE StartTime < $cutoff;
            """;
        command.Parameters.AddWithValue("$cutoff", cutoff.ToString("o"));
        command.ExecuteNonQuery();
    }
}