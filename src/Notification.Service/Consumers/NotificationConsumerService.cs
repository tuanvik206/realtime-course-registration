using System.Text;
using Microsoft.AspNetCore.SignalR;
using Notification.Service.Hubs;
using Notification.Service.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RealtimeCourseRegistration.Contracts.Events;

namespace Notification.Service.Consumers;

public sealed class NotificationConsumerService(
    RabbitMqClient rabbitMq,
    IHubContext<CourseHub> hubContext,
    ILogger<NotificationConsumerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await rabbitMq.GetConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(false, false, consumerDispatchConcurrency: 1), stoppingToken);
                await channel.BasicQosAsync(0, 16, global: false, stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    await HandleDeliveryAsync(channel, delivery, stoppingToken);
                };
                await channel.BasicConsumeAsync(BrokerTopology.NotificationQueue, autoAck: false, consumer, stoppingToken);
                logger.LogInformation("Notification consumer is listening on {Queue}.", BrokerTopology.NotificationQueue);
                delay = TimeSpan.FromSeconds(1);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Notification consumer connection failed; retrying in {DelaySeconds}s.", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        IntegrationEvent integrationEvent;
        try
        {
            var json = Encoding.UTF8.GetString(delivery.Body.Span);
            integrationEvent = IntegrationEventJson.Deserialize(json);
            var validation = IntegrationEventValidator.Validate(integrationEvent);
            if (!validation.IsValid
                || !Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId)
                || messageId != integrationEvent.EventId)
            {
                logger.LogError("Malformed notification event sent to dead letter. EventId={EventId}, ErrorCode={ErrorCode}",
                    integrationEvent.EventId, validation.ErrorCode ?? "MESSAGE_ID_MISMATCH");
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                return;
            }
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException)
        {
            logger.LogError(exception, "Malformed notification payload sent to dead letter.");
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var group = CourseHub.GroupName(integrationEvent.CourseSectionId);
                if (integrationEvent.EventType is IntegrationEventTypes.CourseCreated or IntegrationEventTypes.CourseUpdated)
                {
                    await hubContext.Clients.Group(group).SendAsync("CourseCatalogChanged",
                        new CourseCatalogChanged(integrationEvent.CourseSectionId, integrationEvent.Version), stoppingToken);
                }
                else
                {
                    await hubContext.Clients.Group(group).SendAsync("CourseCapacityUpdated",
                        new CourseCapacityUpdated(integrationEvent.CourseSectionId, integrationEvent.Capacity,
                            integrationEvent.RegisteredCount, integrationEvent.RemainingSeats,
                            integrationEvent.Status, integrationEvent.Version), stoppingToken);
                }
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                logger.LogInformation("Broadcast course snapshot. EventId={EventId}, CourseSectionId={CourseSectionId}, Version={Version}",
                    integrationEvent.EventId, integrationEvent.CourseSectionId, integrationEvent.Version);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (attempt < 3)
            {
                logger.LogWarning(exception, "SignalR dispatch failed. EventId={EventId}, Attempt={Attempt}",
                    integrationEvent.EventId, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "SignalR dispatch retries exhausted; event sent to dead letter. EventId={EventId}",
                    integrationEvent.EventId);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            }
        }
    }
}

public sealed record CourseCapacityUpdated(
    Guid CourseSectionId, int Capacity, int RegisteredCount, int RemainingSeats, string Status, long Version);
public sealed record CourseCatalogChanged(Guid CourseSectionId, long Version);
