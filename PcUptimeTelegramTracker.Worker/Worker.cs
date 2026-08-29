using PcUptimeTelegramTracker.Worker.Models;
using PcUptimeTelegramTracker.Worker.Services;
using PcUptimeTelegramTracker.Worker.Storage;
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly UptimeTrackerService _uptimeTracker;
    private readonly SessionStateStore _sessionStateStore;

    public Worker(
        ILogger<Worker> logger,
        TelegramNotifier telegramNotifier,
        UptimeTrackerService uptimeTracker,
        SessionStateStore sessionStateStore)
    {
        _logger = logger;
        _telegramNotifier = telegramNotifier;
        _uptimeTracker = uptimeTracker;
        _sessionStateStore = sessionStateStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PcUptimeTelegramTracker başlatıldı.");

        _uptimeTracker.LoadTodaysHistory();
        _uptimeTracker.StartLiveWatching();

        await ReportPreviousSessionIfNeeded(stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ReportPreviousSessionIfNeeded(CancellationToken cancellationToken)
    {
        var session = _uptimeTracker.DetermineLastCompletedSession();
        if (session is null) return;

        var lastReported = _sessionStateStore.GetLastReportedSessionEnd();
        if (lastReported.HasValue && session.EndTime <= lastReported.Value)
        {
            // Already reported this session, nothing new to send.
            return;
        }

        // Placeholder message format — will be refined later.
        var status = session.EndedCleanly ? "" : " (beklenmedik şekilde sonlandı)";
        var message =
            $"Önceki oturum{status}: {session.TotalDuration:hh\\:mm} açık kaldı " +
            $"(Uyanık: {session.AwakeDuration:hh\\:mm}, Uykuda: {session.SleepDuration:hh\\:mm})";

        await _telegramNotifier.SendMessageAsync(message, cancellationToken);
        _sessionStateStore.SetLastReportedSessionEnd(session.EndTime);
    }
}