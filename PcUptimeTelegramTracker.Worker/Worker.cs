using PcUptimeTelegramTracker.Worker.Models;
using PcUptimeTelegramTracker.Worker.Services;
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TelegramNotifier _telegramNotifier;

    public Worker(ILogger<Worker> logger, TelegramNotifier telegramNotifier)
    {
        _logger = logger;
        _telegramNotifier = telegramNotifier;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PcUptimeTelegramTracker başlatıldı.");

        // Temporary connectivity test — will be replaced by the real
        // EventLogWatcher-driven summary logic.
        await _telegramNotifier.SendMessageAsync("Servis başarıyla başlatıldı, test mesajı! 🎉", stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}