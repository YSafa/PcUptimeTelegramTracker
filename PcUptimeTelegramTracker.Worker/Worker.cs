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
    private readonly WeeklyReportService _weeklyReportService;

    public Worker(
        ILogger<Worker> logger,
        TelegramNotifier telegramNotifier,
        UptimeTrackerService uptimeTracker,
        SessionStateStore sessionStateStore,
        ProcessUsageCollector processUsageCollector,
        UsageRepository usageRepository,
        WeeklyReportService weeklyReportService)
    {
        _logger = logger;
        _telegramNotifier = telegramNotifier;
        _uptimeTracker = uptimeTracker;
        _sessionStateStore = sessionStateStore;
        _processUsageCollector = processUsageCollector;
        _usageRepository = usageRepository;
        _weeklyReportService = weeklyReportService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("PcUptimeTelegramTracker başlatıldı.");

            _uptimeTracker.LoadTodaysHistory();
            _uptimeTracker.StartLiveWatching();
            _usageRepository.PruneOlderThan(DateTime.Now.AddDays(-7));

            await ReportPreviousSessionIfNeeded(stoppingToken);
            await _weeklyReportService.SendIfDueAsync(stoppingToken);

            var currentSessionStart = _uptimeTracker.GetCurrentSessionStartTime();

            // Fast Startup can resume this exact process across a shutdown/startup
            // cycle instead of restarting it — so we also react to a live boot
            // detection, not just to the one-time startup logic above.
            _uptimeTracker.BootDetected += async (timestamp) =>
            {
                try
                {
                    _logger.LogInformation("Canlı açılış tespit edildi (muhtemelen Hızlı Başlatma sonrası devam eden süreç).");
                    await ReportPreviousSessionIfNeeded(stoppingToken);
                    currentSessionStart = timestamp;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Canlı açılış sonrası oturum raporlama hatası.");
                }
            };

            var minuteCounter = 0;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _processUsageCollector.SampleOnce(currentSessionStart);

                minuteCounter++;
                if (minuteCounter >= 1440)
                {
                    minuteCounter = 0;
                    await _weeklyReportService.SendIfDueAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Servis normal şekilde durduruluyor.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Servis beklenmedik bir hata nedeniyle çöktü.");
            throw;
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

        var statusLine = session.EndedCleanly ? "" : "\n⚠️ Beklenmedik şekilde sonlandı";

        var message =
            $"💻 Önceki Oturum Özeti{statusLine}\n" +
            $"🕐 {session.StartTime:dd.MM.yyyy HH:mm:ss} → {session.EndTime:dd.MM.yyyy HH:mm:ss}\n" +
            $"⏱️ Toplam: {DurationFormatter.Format(session.TotalDuration)}\n" +
            $"🟢 Uyanık: {DurationFormatter.Format(session.AwakeDuration)}\n" +
            $"🌙 Uykuda: {DurationFormatter.Format(session.SleepDuration)}";

        if (topApps.Count > 0 && totalSampledSeconds > 0)
        {
            message += "\n\n🔥 En çok kaynak tüketen uygulamalar:\n" +
                       string.Join("\n", topApps.Select((app, i) =>
                       {
                           var avgPercent = (app.CpuTime.TotalSeconds / totalSampledSeconds) * 100;
                           return $"{i + 1}. {app.ProcessName} — {DurationFormatter.Format(app.CpuTime)} (ort. %{avgPercent:0.0})";
                       }));
        }

        var sent = await _telegramNotifier.SendMessageWithRetryAsync(message, cancellationToken: cancellationToken);
        if (sent)
        {
            _sessionStateStore.SetLastReportedSessionEnd(session.EndTime);
        }
    }
}