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
            return;
        }

        var status = session.EndedCleanly ? "" : " (beklenmedik şekilde sonlandı)";
        var message =
            $"Önceki oturum{status}\n" +
            $"{session.StartTime:dd.MM.yyyy HH:mm:ss} - {session.EndTime:dd.MM.yyyy HH:mm:ss} arası açık kaldı " +
            $"(toplam {session.TotalDuration:hh\\:mm\\:ss})\n" +
            $"Uyanık: {session.AwakeDuration:hh\\:mm\\:ss}, Uykuda: {session.SleepDuration:hh\\:mm\\:ss}";

        await _telegramNotifier.SendMessageAsync(message, cancellationToken);
        _sessionStateStore.SetLastReportedSessionEnd(session.EndTime);
    }
}