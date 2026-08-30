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
            """;
        command.ExecuteNonQuery();
    }

    // Adds `delta` CPU ticks for a process within a session, creating the row
    // if it doesn't exist yet. Called every ~60s from the sampling loop, so
    // data survives a crash instead of only being written at session end.
    public void AddAppUsage(DateTime sessionStartTime, string processName, TimeSpan delta)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppUsage (SessionStartTime, ProcessName, CpuTimeTicks)
            VALUES ($sessionStart, $processName, $ticks)
            ON CONFLICT(SessionStartTime, ProcessName)
            DO UPDATE SET CpuTimeTicks = CpuTimeTicks + excluded.CpuTimeTicks;
            """;
        command.Parameters.AddWithValue("$sessionStart", sessionStartTime.ToString("o"));
        command.Parameters.AddWithValue("$processName", processName);
        command.Parameters.AddWithValue("$ticks", delta.Ticks);
        command.ExecuteNonQuery();
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