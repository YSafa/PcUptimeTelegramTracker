using System.Diagnostics.Eventing.Reader;
using PcUptimeTelegramTracker.Worker.Models;

namespace PcUptimeTelegramTracker.Worker.Services;

// Machine power state, inferred from Windows Event Log entries.
public enum PowerState
{
    Awake,
    Asleep,
    Unknown
}

public class UptimeTrackerService : IDisposable
{
    private const string SystemLogChannel = "System";

    // Kernel-Power: 42 = entering sleep, 1 = resumed from sleep, 41 = unexpected shutdown/reboot.
    // EventLog: 6005 = system started (clean boot), 6006 = clean shutdown.
    private static readonly int[] RelevantEventIds = { 42, 1, 41, 6005, 6006 };

    private readonly ILogger<UptimeTrackerService> _logger;
    private readonly DailyUsageSummary _summary = new();

    private PowerState _currentState = PowerState.Unknown;
    private DateTime _lastTransitionTime;
    private EventLogWatcher? _watcher;

    public UptimeTrackerService(ILogger<UptimeTrackerService> logger)
    {
        _logger = logger;
    }

    public DailyUsageSummary Summary => _summary;

    // Step 1: reconstruct today's timeline from the event log so a service
    // restart doesn't lose data collected earlier in the day.
    public void LoadTodaysHistory()
    {
        var todayStart = DateTime.Today;

        // First, find the state we were in right before midnight, so we know
        // what to assume if the machine was already awake/asleep at day start.
        var stateBeforeToday = FindMostRecentStateBefore(todayStart);
        _currentState = stateBeforeToday;
        _lastTransitionTime = todayStart;

        // Then walk through everything that happened since midnight.
        foreach (var (timestamp, eventId) in ReadEventsSince(todayStart))
        {
            ApplyTransition(timestamp, eventId);
        }

        _logger.LogInformation(
            "Geçmiş yüklendi. Uyanık: {Awake}, Uykuda: {Sleep}, Bilinmiyor: {Unknown}",
            _summary.TotalAwakeTime, _summary.TotalSleepTime, _summary.TotalUnknownTime);
    }

    // Step 2: subscribe to new events going forward — no polling, purely push-based.
    public void StartLiveWatching()
    {
        var query = BuildQuery(DateTime.Now);
        var eventLogQuery = new EventLogQuery(SystemLogChannel, PathType.LogName, query);
        _watcher = new EventLogWatcher(eventLogQuery);
        _watcher.EventRecordWritten += OnEventRecordWritten;
        _watcher.Enabled = true;

        _logger.LogInformation("Canlı olay dinleme başladı.");
    }

    private void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventRecord is null) return;

        var timestamp = e.EventRecord.TimeCreated ?? DateTime.Now;
        var eventId = e.EventRecord.Id;
        ApplyTransition(timestamp, eventId);
    }

    private void ApplyTransition(DateTime timestamp, int eventId)
    {
        // Accumulate the duration of the state we were in until this event.
        var elapsed = timestamp - _lastTransitionTime;
        if (elapsed > TimeSpan.Zero)
        {
            switch (_currentState)
            {
                case PowerState.Awake: _summary.TotalAwakeTime += elapsed; break;
                case PowerState.Asleep: _summary.TotalSleepTime += elapsed; break;
                case PowerState.Unknown: _summary.TotalUnknownTime += elapsed; break;
            }
        }

        var previousState = _currentState;

        // Determine the new state based on the event that just occurred.
        _currentState = eventId switch
        {
            42 => PowerState.Asleep,           // going to sleep
            1 or 6005 => PowerState.Awake,      // resumed or clean boot
            41 => PowerState.Unknown,           // unexpected shutdown — unclear what happened next
            6006 => PowerState.Unknown,         // clean shutdown — service isn't running to observe more anyway
            _ => _currentState
        };
        _lastTransitionTime = timestamp;

        // Temporary — remove once we've confirmed live watching works correctly.
       /* _logger.LogInformation(
            "Durum değişti: EventID={EventId}, {Previous} -> {New}, Geçen süre: {Elapsed}",
            eventId, previousState, _currentState, elapsed);*/
    }

    private PowerState FindMostRecentStateBefore(DateTime cutoff)
    {
        // Look back up to 7 days for the last relevant event before "cutoff".
        var lookbackStart = cutoff.AddDays(-7);
        var query = BuildQuery(lookbackStart, cutoff);
        var eventLogQuery = new EventLogQuery(SystemLogChannel, PathType.LogName, query) { ReverseDirection = true };

        using var reader = new EventLogReader(eventLogQuery);
        var record = reader.ReadEvent();
        if (record is null) return PowerState.Unknown;

        return record.Id switch
        {
            42 => PowerState.Asleep,
            1 or 6005 => PowerState.Awake,
            _ => PowerState.Unknown
        };
    }

    private IEnumerable<(DateTime Timestamp, int EventId)> ReadEventsSince(DateTime since)
    {
        var query = BuildQuery(since);
        var eventLogQuery = new EventLogQuery(SystemLogChannel, PathType.LogName, query);
        using var reader = new EventLogReader(eventLogQuery);

        var results = new List<(DateTime, int)>();
        EventRecord? record;
        while ((record = reader.ReadEvent()) != null)
        {
            if (record.TimeCreated.HasValue)
                results.Add((record.TimeCreated.Value, record.Id));
        }

        // EventLogReader already returns events in chronological order by default
        // (ReverseDirection defaults to false), so no reversal is needed here.
        return results;
    }

    private string BuildQuery(DateTime from, DateTime? to = null)
    {
        var idFilter = string.Join(" or ", RelevantEventIds.Select(id => $"EventID={id}"));
        var fromUtc = from.ToUniversalTime().ToString("o");
        var timeFilter = $"TimeCreated[@SystemTime>='{fromUtc}'";
        if (to.HasValue)
        {
            timeFilter += $" and @SystemTime<='{to.Value.ToUniversalTime():o}'";
        }
        timeFilter += "]";

        return $"*[System[({idFilter}) and {timeFilter}]]";
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}