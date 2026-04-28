using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.MessageBus;

namespace TicketTriageAgent.WorkerService;

public class Worker(ILogger<Worker> logger, IConnection connection) : BackgroundService
{
    private const string ExchangeName = "ticket.created";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Fanout, durable: true,
            cancellationToken: stoppingToken);

        var queueResult = await channel.QueueDeclareAsync(exclusive: true, autoDelete: true,
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(queueResult.QueueName, ExchangeName, routingKey: string.Empty,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var ticketEvent = JsonSerializer.Deserialize<TicketCreatedEvent>(json);

            if (ticketEvent is not null)
            {
                logger.LogInformation(
                    "TicketCreatedEvent received — Id={Id}, Title={Title}, Priority={Priority}, Status={Status}, UserId={UserId}, CreatedAt={CreatedAt}",
                    ticketEvent.Id, ticketEvent.Title, ticketEvent.Priority, ticketEvent.Status,
                    ticketEvent.UserId, ticketEvent.CreatedAt);

                // TODO: Add triage logic here
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        };

        await channel.BasicConsumeAsync(queueResult.QueueName, autoAck: false, consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("Worker started, listening on fanout exchange '{Exchange}'", ExchangeName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Worker stopping.");
        }
    }
}
