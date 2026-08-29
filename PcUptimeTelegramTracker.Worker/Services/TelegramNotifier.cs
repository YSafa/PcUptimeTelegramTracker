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

    public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.SendMessage(chatId: _chatId, text: message, cancellationToken: cancellationToken);
            _logger.LogInformation("Telegram mesajı gönderildi.");
        }
        catch (Exception ex)
        {
            // Network issues or an invalid token/chat id shouldn't crash the whole service.
            _logger.LogError(ex, "Telegram mesajı gönderilirken hata oluştu.");
        }
    }
}