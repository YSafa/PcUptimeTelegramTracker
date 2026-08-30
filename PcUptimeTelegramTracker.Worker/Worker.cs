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
        _logger.LogInformation("PcUptimeTelegramTracker başlatıldı.");

        _uptimeTracker.LoadTodaysHistory();
        _uptimeTracker.StartLiveWatching();
        _usageRepository.PruneOlderThan(DateTime.Now.AddDays(-7));

        await ReportPreviousSessionIfNeeded(stoppingToken);
        await _weeklyReportService.SendIfDueAsync(stoppingToken);

        var currentSessionStart = _uptimeTracker.GetCurrentSessionStartTime();

        var minuteCounter = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _processUsageCollector.SampleOnce(currentSessionStart);

            // Check the weekly report once a day (every 1440th minute tick)
            // instead of every minute — the check itself is cheap, but no
            // need to hit the DB/state file that often.
            minuteCounter++;
            if (minuteCounter >= 1440)
            {
                minuteCounter = 0;
                await _weeklyReportService.SendIfDueAsync(stoppingToken);
            }
        }
    }

    private async Task ReportPreviousSessionIfNeeded(CancellationToken cancellationToken)
    {
        // ... existing content, unchanged ...
    }
}