using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Shared.MessageBus;

namespace WebApplication.API.Services;

public class RabbitMqTicketPublisher : IHostedService, IAsyncDisposable
{
    private const string ExchangeName = "ticket.created";

    private readonly ILogger<RabbitMqTicketPublisher> _logger;
    private readonly IConnection _connection;
    private IChannel? _channel;

    public RabbitMqTicketPublisher(ILogger<RabbitMqTicketPublisher> logger, IConnection connection)
    {
        _logger = logger;
        _connection = connection;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await _channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Fanout, durable: true,
            cancellationToken: cancellationToken);
        _logger.LogInformation("RabbitMQ publisher connected, exchange '{Exchange}'", ExchangeName);
    }

    public async Task PublishTicketCreatedAsync(TicketCreatedEvent ticketEvent)
    {
        if (_channel is null)
            throw new InvalidOperationException("RabbitMQ publisher is not initialized.");

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ticketEvent));
        await _channel.BasicPublishAsync(ExchangeName, routingKey: string.Empty, body: body);
        _logger.LogInformation("Published TicketCreatedEvent for ticket Id={Id}", ticketEvent.Id);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
    }
}
