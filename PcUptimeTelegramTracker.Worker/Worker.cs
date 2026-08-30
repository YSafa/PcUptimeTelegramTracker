using PcUptimeTelegramTracker.Worker.Models;
using PcUptimeTelegramTracker.Worker.Services;
using PcUptimeTelegramTracker.Worker.Storage;
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly UptimeTrackerService _uptimeTracker;
    private readonly SessionStateStore _sessionStateStore;
    private readonly ProcessUsageCollector _processUsageCollector;
    private readonly UsageRepository _usageRepository;

    public Worker(
        ILogger<Worker> logger,
        TelegramNotifier telegramNotifier,
        UptimeTrackerService uptimeTracker,
        SessionStateStore sessionStateStore,
        ProcessUsageCollector processUsageCollector,
        UsageRepository usageRepository)
    {
        _logger = logger;
        _telegramNotifier = telegramNotifier;
        _uptimeTracker = uptimeTracker;
        _sessionStateStore = sessionStateStore;
        _processUsageCollector = processUsageCollector;
        _usageRepository = usageRepository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PcUptimeTelegramTracker başlatıldı.");

        _uptimeTracker.LoadTodaysHistory();
        _uptimeTracker.StartLiveWatching();
        _usageRepository.PruneOlderThan(DateTime.Now.AddDays(-7));

        await ReportPreviousSessionIfNeeded(stoppingToken);

        var currentSessionStart = _uptimeTracker.GetCurrentSessionStartTime();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _processUsageCollector.SampleOnce(currentSessionStart);
        }
    }

    private async Task ReportPreviousSessionIfNeeded(CancellationToken cancellationToken)
    {
        var session = _uptimeTracker.DetermineLastCompletedSession();
        if (session is null) return;

        var lastReported = _sessionStateStore.GetLastReportedSessionEnd();
        if (lastReported.HasValue && session.EndTime <= lastReported.Value)
        {
            return;
        }

        _usageRepository.SaveSession(
            session.StartTime, session.EndTime, session.AwakeDuration, session.SleepDuration, session.EndedCleanly);

        var topApps = _usageRepository.GetTopApps(session.StartTime, 5);
        var sampleCount = _usageRepository.GetSampleCount(session.StartTime);
        var totalSampledSeconds = sampleCount * 60.0 * Environment.ProcessorCount;

        var status = session.EndedCleanly ? "" : " (beklenmedik şekilde sonlandı)";
        var message =
            $"Önceki oturum{status}\n" +
            $"{session.StartTime:dd.MM.yyyy HH:mm:ss} - {session.EndTime:dd.MM.yyyy HH:mm:ss} arası açık kaldı " +
            $"(toplam {session.TotalDuration:hh\\:mm\\:ss})\n" +
            $"Uyanık: {session.AwakeDuration:hh\\:mm\\:ss}, Uykuda: {session.SleepDuration:hh\\:mm\\:ss}";

        if (topApps.Count > 0 && totalSampledSeconds > 0)
        {
            message += "\n\nEn çok kaynak tüketen uygulamalar:\n" +
                       string.Join("\n", topApps.Select((app, i) =>
                       {
                           var avgPercent = (app.CpuTime.TotalSeconds / totalSampledSeconds) * 100;
                           return $"{i + 1}. {app.ProcessName} — {app.CpuTime:hh\\:mm\\:ss} (ort. %{avgPercent:0.0})";
                       }));
        }

        await _telegramNotifier.SendMessageAsync(message, cancellationToken);
        _sessionStateStore.SetLastReportedSessionEnd(session.EndTime);
    }
}