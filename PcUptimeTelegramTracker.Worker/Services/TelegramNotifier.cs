using Microsoft.Extensions.Options;
using Telegram.Bot;
using PcUptimeTelegramTracker.Worker.Models;

namespace PcUptimeTelegramTracker.Worker.Services;

public class TelegramNotifier
{
    private readonly TelegramBotClient _client;
    private readonly string _chatId;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(IOptions<TelegramSettings> settings, ILogger<TelegramNotifier> logger)
    {
        _client = new TelegramBotClient(settings.Value.BotToken);
        _chatId = settings.Value.ChatId;
        _logger = logger;
    }

    public async Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.SendMessage(chatId: _chatId, text: message, cancellationToken: cancellationToken);
            _logger.LogInformation("Telegram mesajı gönderildi.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram mesajı gönderilirken hata oluştu.");
            return false;
        }
    }
    
    public async Task<bool> SendMessageWithRetryAsync(
        string message, int maxAttempts = 5, TimeSpan? delayBetweenAttempts = null, CancellationToken cancellationToken = default)
    {
        delayBetweenAttempts ??= TimeSpan.FromSeconds(20);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sent = await SendMessageAsync(message, cancellationToken);
            if (sent) return true;

            if (attempt < maxAttempts)
            {
                _logger.LogWarning("Gönderim denemesi {Attempt}/{Max} başarısız, {Delay} sonra tekrar denenecek.",
                    attempt, maxAttempts, delayBetweenAttempts.Value);
                await Task.Delay(delayBetweenAttempts.Value, cancellationToken);
            }
        }

        _logger.LogError("Tüm gönderim denemeleri başarısız oldu.");
        return false;
    }
    
}