public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PcUptimeTelegramTracker başlatıldı.");

        // EventLogWatcher subscription will be added here later.
        // For now, the service just stays alive.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}