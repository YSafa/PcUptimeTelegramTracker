namespace PcUptimeTelegramTracker.Worker.Models;

// Strongly-typed binding for the "Telegram" section in appsettings.json
public class TelegramSettings
{
    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
}