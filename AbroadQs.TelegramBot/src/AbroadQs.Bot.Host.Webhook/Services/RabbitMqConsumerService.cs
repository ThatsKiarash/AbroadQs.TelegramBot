using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AbroadQs.Bot.Host.Webhook.Services;

public sealed class RabbitMqConsumerService : BackgroundService
{
    private readonly RabbitMqOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqConsumerService> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private string? _consumerTag;

    public RabbitMqConsumerService(
        IOptions<RabbitMqOptions> options,
        IServiceProvider serviceProvider,
        ILogger<RabbitMqConsumerService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ consumer error; reconnecting in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private Task ConnectAndConsumeAsync(CancellationToken stoppingToken)
    {
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
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += OnMessageReceived;
        _consumerTag = _channel.BasicConsume(_options.QueueName, autoAck: false, consumer: consumer);
        _logger.LogInformation("RabbitMQ consumer started for queue {Queue}", _options.QueueName);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stoppingToken.Register(() => tcs.TrySetResult());
        return tcs.Task;
    }

    private void OnMessageReceived(object? sender, BasicDeliverEventArgs ea)
    {
        var channel = _channel;
        if (channel == null || !channel.IsOpen) return;
        try
        {
            // وب‌هوک همین آپدیت را قبلاً dispatch کرده و جواب فرستاده؛ اینجا فقط ack می‌کنیم تا صف خالی شود (جواب تکراری نفرستیم)
            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ack message from queue");
            try { channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true); } catch { /* ignore */ }
        }
    }

    public override void Dispose()
    {
        try { _channel?.Close(); } catch { /* ignore */ }
        try { _connection?.Close(); } catch { /* ignore */ }
        base.Dispose();
    }
}
