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

    // (EventID, Provider) pairs we care about. We filter by provider too,
    // because event IDs are not unique across providers within the System log
    // (e.g. ID 27 also exists under an unrelated Eventlog-channel-init source).
    //
    // 42/1/41  -> Kernel-Power: sleep, resume, unexpected shutdown.
    // 27       -> Kernel-Boot: system boot, fired for ANY boot type
    //             (cold boot, Fast Startup, resume-from-hibernate) — unlike
    //             6005, which Fast Startup can skip entirely.
    // 1074     -> User32: user clicked Shut down/Restart. Fired at click time,
    //             before any hibernation write, so it's reliable even with
    //             Fast Startup enabled — unlike 6006.
    private static readonly (int EventId, string Provider)[] RelevantEvents =
    {
        (42, "Microsoft-Windows-Kernel-Power"),
        (1, "Microsoft-Windows-Kernel-Power"),
        (41, "Microsoft-Windows-Kernel-Power"),
        (27, "Microsoft-Windows-Kernel-Boot"),
        (1074, "USER32")
    };

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

        var stateBeforeToday = FindMostRecentStateBefore(todayStart);
        _currentState = stateBeforeToday;
        _lastTransitionTime = todayStart;

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

        _currentState = eventId switch
        {
            42 => PowerState.Asleep,     // entering sleep
            1 => PowerState.Awake,        // resumed from sleep
            27 => PowerState.Awake,       // system booted (any boot type)
            41 => PowerState.Unknown,     // unexpected shutdown
            1074 => PowerState.Unknown,   // user-initiated shutdown/restart
            _ => _currentState
        };
        _lastTransitionTime = timestamp;
    }

    private PowerState FindMostRecentStateBefore(DateTime cutoff)
    {
        var lookbackStart = cutoff.AddDays(-7);
        var query = BuildQuery(lookbackStart, cutoff);
        var eventLogQuery = new EventLogQuery(SystemLogChannel, PathType.LogName, query) { ReverseDirection = true };

        using var reader = new EventLogReader(eventLogQuery);
        var record = reader.ReadEvent();
        if (record is null) return PowerState.Unknown;

        return record.Id switch
        {
            42 => PowerState.Asleep,
            1 or 27 => PowerState.Awake,
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

        // EventLogReader returns events in chronological order by default
        // (ReverseDirection defaults to false) — no reversal needed.
        return results;
    }

    private string BuildQuery(DateTime from, DateTime? to = null)
    {
        var idFilter = string.Join(" or ",
            RelevantEvents.Select(e => $"(Provider[@Name='{e.Provider}'] and EventID={e.EventId})"));

        var fromUtc = from.ToUniversalTime().ToString("o");
        var timeFilter = $"TimeCreated[@SystemTime>='{fromUtc}'";
        if (to.HasValue)
        {
            timeFilter += $" and @SystemTime<='{to.Value.ToUniversalTime():o}'";
        }
        timeFilter += "]";

        return $"*[System[({idFilter}) and {timeFilter}]]";
    }

    // Reconstructs the most recently completed session (boot -> shutdown/crash)
    // that happened before the current boot, using event log history.
    public SessionSummary? DetermineLastCompletedSession()
    {
        var lookbackStart = DateTime.Now.AddDays(-30);
        var events = ReadEventsSince(lookbackStart).ToList();

        var currentBootIndex = -1;
        for (var i = events.Count - 1; i >= 0; i--)
        {
            if (events[i].EventId == 27) { currentBootIndex = i; break; }
        }
        if (currentBootIndex <= 0) return null;

        var sessionEndEvent = events[currentBootIndex - 1];
        var sessionEndTime = sessionEndEvent.Timestamp;
        
        // Only a genuine EventID 41 (Kernel-Power unexpected shutdown) counts as "unclean".
        // Both 1074 (user clicked shut down/restart) and 42 (Fast Startup's hybrid
        // shutdown enters a sleep-like state as its final logged step) are normal endings.
        var endedCleanly = sessionEndEvent.EventId != 41;

        var startIndex = -1;
        for (var i = currentBootIndex - 1; i >= 0; i--)
        {
            if (events[i].EventId == 27) { startIndex = i; break; }
        }
        if (startIndex == -1) return null;

        var sessionStartTime = events[startIndex].Timestamp;

        var awake = TimeSpan.Zero;
        var asleep = TimeSpan.Zero;
        var state = PowerState.Awake;
        var lastTime = sessionStartTime;

        for (var i = startIndex; i < currentBootIndex; i++)
        {
            var (timestamp, eventId) = events[i];
            if (eventId is not (42 or 1)) continue;

            var elapsed = timestamp - lastTime;
            if (elapsed > TimeSpan.Zero)
            {
                if (state == PowerState.Awake) awake += elapsed; else asleep += elapsed;
            }
            state = eventId == 42 ? PowerState.Asleep : PowerState.Awake;
            lastTime = timestamp;
        }

        var remaining = sessionEndTime - lastTime;
        if (remaining > TimeSpan.Zero)
        {
            if (state == PowerState.Awake) awake += remaining; else asleep += remaining;
        }

        return new SessionSummary
        {
            StartTime = sessionStartTime,
            EndTime = sessionEndTime,
            AwakeDuration = awake,
            SleepDuration = asleep,
            EndedCleanly = endedCleanly
        };
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}