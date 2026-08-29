using System.Text.Json;

namespace PcUptimeTelegramTracker.Worker.Storage;

// Persists which session we last reported to Telegram, so restarts within
// the same boot session don't resend the same summary.
// Uses a plain JSON file under ProgramData — no SQLite needed for a single value.
public class SessionStateStore
{
    private readonly string _filePath;

    public SessionStateStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PcUptimeTelegramTracker");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "state.json");
    }

    public DateTime? GetLastReportedSessionEnd()
    {
        if (!File.Exists(_filePath)) return null;

        var json = File.ReadAllText(_filePath);
        var data = JsonSerializer.Deserialize<StateData>(json);
        return data?.LastReportedSessionEnd;
    }

    public void SetLastReportedSessionEnd(DateTime value)
    {
        var data = new StateData { LastReportedSessionEnd = value };
        File.WriteAllText(_filePath, JsonSerializer.Serialize(data));
    }

    private class StateData
    {
        public DateTime? LastReportedSessionEnd { get; set; }
    }
}