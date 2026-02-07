using Telegram.Bot.Types;

namespace AbroadQs.Bot.Host.Webhook.Services;

public interface IRabbitMqPublisher
{
    Task PublishAsync(Update update, CancellationToken cancellationToken = default);
}
