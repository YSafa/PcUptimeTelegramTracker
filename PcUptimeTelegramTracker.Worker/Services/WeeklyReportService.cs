using PcUptimeTelegramTracker.Worker.Storage;

namespace PcUptimeTelegramTracker.Worker.Services;

// Checks once a day whether 7 days have passed since the last weekly
// summary, and if so, aggregates the past week's sessions and app usage
// into a single Telegram message.
public class WeeklyReportService
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromDays(7);

    private readonly UsageRepository _usageRepository;
    private readonly SessionStateStore _stateStore;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly ILogger<WeeklyReportService> _logger;

    public WeeklyReportService(
        UsageRepository usageRepository,
        SessionStateStore stateStore,
        TelegramNotifier telegramNotifier,
        ILogger<WeeklyReportService> logger)
    {
        _usageRepository = usageRepository;
        _stateStore = stateStore;
        _telegramNotifier = telegramNotifier;
        _logger = logger;
    }

    public async Task SendIfDueAsync(CancellationToken cancellationToken)
    {
        var lastSent = _stateStore.GetLastWeeklyReportSent();

        // First time this feature runs: start the 7-day clock from now,
        // rather than immediately sending a report for data we might not
        // fully have (e.g. right after installing the service).
        if (lastSent is null)
        {
            _stateStore.SetLastWeeklyReportSent(DateTime.Now);
            _logger.LogInformation("Haftalık özet için başlangıç zamanı ayarlandı.");
            return;
        }

        if (DateTime.Now - lastSent.Value < ReportInterval)
        {
            return;
        }

        var cutoff = lastSent.Value;
        var (sessionCount, awake, sleep) = _usageRepository.GetSessionsSummarySince(cutoff);

        if (sessionCount == 0)
        {
            // Nothing happened this week (machine was off the whole time, etc.)
            // — still move the clock forward so we don't check every tick.
            _stateStore.SetLastWeeklyReportSent(DateTime.Now);
            return;
        }

        var topApps = _usageRepository.GetTopAppsSince(cutoff, 5);
        var totalSamples = _usageRepository.GetTotalSampleCountSince(cutoff);
        var totalSampledSeconds = totalSamples * 60.0 * Environment.ProcessorCount;

        var message =
            $"📈 Haftalık Özet\n" +
            $"🗓️ {cutoff:dd.MM.yyyy} → {DateTime.Now:dd.MM.yyyy}\n" +
            $"🔢 {sessionCount} oturum\n" +
            $"⏱️ Toplam açık kalma: {DurationFormatter.Format(awake + sleep)}\n" +
            $"🟢 Uyanık: {DurationFormatter.Format(awake)}\n" +
            $"🌙 Uykuda: {DurationFormatter.Format(sleep)}";

        if (topApps.Count > 0 && totalSampledSeconds > 0)
        {
            message += "\n\n🔥 En çok kaynak tüketen uygulamalar:\n" +
                       string.Join("\n", topApps.Select((app, i) =>
                       {
                           var avgPercent = (app.CpuTime.TotalSeconds / totalSampledSeconds) * 100;
                           return $"{i + 1}. {app.ProcessName} — {DurationFormatter.Format(app.CpuTime)} (ort. %{avgPercent:0.0})";
                       }));
        }

        await _telegramNotifier.SendMessageAsync(message, cancellationToken);
        _stateStore.SetLastWeeklyReportSent(DateTime.Now);
    }
}