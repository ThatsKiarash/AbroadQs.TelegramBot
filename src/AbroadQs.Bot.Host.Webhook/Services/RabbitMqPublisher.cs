using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Telegram.Bot.Types;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class RabbitMqPublisher : IRabbitMqPublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly object _lock = new();

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private IModel GetChannel()
    {
        if (_channel != null && _channel.IsOpen)
            return _channel;
        lock (_lock)
        {
            if (_channel != null && _channel.IsOpen)
                return _channel;
            _channel?.Dispose();
            _connection?.Close();
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password
            };
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.QueueDeclare(_options.QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            return _channel;
        }
    }

    public Task PublishAsync(Update update, CancellationToken cancellationToken = default)
    {
        var channel = GetChannel();
        var json = System.Text.Json.JsonSerializer.Serialize(update);
        var body = Encoding.UTF8.GetBytes(json);
        var props = channel.CreateBasicProperties();
        channel.BasicPublish("", _options.QueueName, mandatory: false, props, new ReadOnlyMemory<byte>(body));
        _logger.LogDebug("Published update {UpdateId} to queue {Queue}", update.Id, _options.QueueName);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _channel?.Dispose(); } catch { /* ignore */ }
        try { _connection?.Close(); } catch { /* ignore */ }
    }
}
