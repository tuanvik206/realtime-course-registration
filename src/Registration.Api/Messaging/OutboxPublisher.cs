using System.Text;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RealtimeCourseRegistration.Contracts.Capacity.V1;
using RealtimeCourseRegistration.Contracts.Events;
using Capacity = RealtimeCourseRegistration.Contracts.Capacity.V1;

namespace Registration.Api.Messaging;

public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    RabbitMqClient rabbitMq,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromMilliseconds(500);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var publishedAny = await PublishBatchAsync(stoppingToken);
                delay = TimeSpan.FromMilliseconds(500);
                if (!publishedAny)
                {
                    await Task.Delay(delay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Outbox relay iteration failed; retrying after {DelayMs}ms.", delay.TotalMilliseconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 30_000));
            }
        }
    }

    private async Task<bool> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var capacity = scope.ServiceProvider.GetRequiredService<Capacity.CourseCapacity.CourseCapacityClient>();
        var headers = new Metadata { { "x-correlation-id", Guid.NewGuid().ToString() } };
        var pending = await capacity.GetPendingEventsAsync(new PendingEventsRequest { BatchSize = 50 },
            headers, DateTime.UtcNow.AddSeconds(3), cancellationToken);
        if (pending.Events.Count == 0)
        {
            return false;
        }

        var connection = await rabbitMq.GetConnectionAsync(cancellationToken);
        await RabbitMqTopology.DeclareAsync(connection, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        foreach (var item in pending.Events)
        {
            var integrationEvent = IntegrationEventJson.Deserialize(item.PayloadJson);
            var validation = IntegrationEventValidator.Validate(integrationEvent);
            var routingKey = IntegrationEventTypes.GetRoutingKey(integrationEvent.EventType);
            if (!validation.IsValid || routingKey is null || integrationEvent.EventId.ToString() != item.EventId)
            {
                logger.LogError("Outbox event is invalid. EventId={EventId}, ErrorCode={ErrorCode}",
                    item.EventId, validation.ErrorCode ?? "EVENT_ID_MISMATCH");
                throw new InvalidDataException("An outbox event failed contract validation.");
            }

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                MessageId = integrationEvent.EventId.ToString(),
                Persistent = true
            };
            var body = Encoding.UTF8.GetBytes(item.PayloadJson);
            await channel.BasicPublishAsync(BrokerTopology.Exchange, routingKey, mandatory: true,
                properties, body, cancellationToken);

            await capacity.MarkEventPublishedAsync(new MarkEventPublishedRequest { EventId = item.EventId },
                headers, DateTime.UtcNow.AddSeconds(3), cancellationToken);
            logger.LogInformation("Published outbox event. EventId={EventId}, EventType={EventType}, Version={Version}",
                integrationEvent.EventId, integrationEvent.EventType, integrationEvent.Version);
        }
        return true;
    }
}
