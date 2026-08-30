using System.Text.Json;

namespace PcUptimeTelegramTracker.Worker.Storage;

// Persists small pieces of state that need to survive service restarts:
// which session we last reported, and when we last sent a weekly summary.
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

    public DateTime? GetLastReportedSessionEnd() => Load().LastReportedSessionEnd;

    public void SetLastReportedSessionEnd(DateTime value)
    {
        var data = Load();
        data.LastReportedSessionEnd = value;
        Save(data);
    }

    public DateTime? GetLastWeeklyReportSent() => Load().LastWeeklyReportSent;

    public void SetLastWeeklyReportSent(DateTime value)
    {
        var data = Load();
        data.LastWeeklyReportSent = value;
        Save(data);
    }

    private StateData Load()
    {
        if (!File.Exists(_filePath)) return new StateData();
        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<StateData>(json) ?? new StateData();
    }

    private void Save(StateData data)
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(data));
    }

    private class StateData
    {
        public DateTime? LastReportedSessionEnd { get; set; }
        public DateTime? LastWeeklyReportSent { get; set; }
    }
}