using System.Text;
using Audit.Worker.Data;
using Audit.Worker.Data.Entities;
using Audit.Worker.Messaging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RealtimeCourseRegistration.Contracts.Events;

namespace Audit.Worker.Consumers;

public sealed class AuditConsumerService(
    IServiceScopeFactory scopeFactory,
    RabbitMqClient rabbitMq,
    ILogger<AuditConsumerService> logger) : BackgroundService
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
                    await HandleDeliveryAsync(channel, delivery, stoppingToken);
                await channel.BasicConsumeAsync(BrokerTopology.AuditQueue, autoAck: false, consumer, stoppingToken);
                logger.LogInformation("Audit consumer is listening on {Queue}.", BrokerTopology.AuditQueue);
                delay = TimeSpan.FromSeconds(1);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Audit consumer connection failed; retrying in {DelaySeconds}s.", delay.TotalSeconds);
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
            integrationEvent = IntegrationEventJson.Deserialize(Encoding.UTF8.GetString(delivery.Body.Span));
            var validation = IntegrationEventValidator.Validate(integrationEvent);
            if (!validation.IsValid
                || !Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId)
                || messageId != integrationEvent.EventId)
            {
                logger.LogError("Malformed audit event sent to dead letter. EventId={EventId}, ErrorCode={ErrorCode}",
                    integrationEvent.EventId, validation.ErrorCode ?? "MESSAGE_ID_MISMATCH");
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                return;
            }
        }
        catch (System.Text.Json.JsonException exception)
        {
            logger.LogError(exception, "Malformed audit payload sent to dead letter.");
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
                var receivedAt = DateTime.UtcNow;
                var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO audit_logs
                        (event_id, event_type, schema_version, correlation_id, actor_id, student_id,
                         registration_id, course_section_id, capacity, registered_count, remaining_seats,
                         status, version, occurred_at, received_at)
                    VALUES
                        ({integrationEvent.EventId}, {integrationEvent.EventType}, {integrationEvent.SchemaVersion},
                         {integrationEvent.CorrelationId}, {integrationEvent.ActorId}, {integrationEvent.StudentId},
                         {integrationEvent.RegistrationId}, {integrationEvent.CourseSectionId}, {integrationEvent.Capacity},
                         {integrationEvent.RegisteredCount}, {integrationEvent.RemainingSeats}, {integrationEvent.Status},
                         {integrationEvent.Version}, {integrationEvent.OccurredAt.UtcDateTime}, {receivedAt})
                    ON CONFLICT (event_id) DO NOTHING
                    """, stoppingToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                logger.LogInformation("Persisted audit event. EventId={EventId}, Inserted={Inserted}, CorrelationId={CorrelationId}",
                    integrationEvent.EventId, inserted == 1, integrationEvent.CorrelationId);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (attempt < 3)
            {
                logger.LogWarning(exception, "Audit persist failed. EventId={EventId}, Attempt={Attempt}",
                    integrationEvent.EventId, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Audit retries exhausted; event sent to dead letter. EventId={EventId}",
                    integrationEvent.EventId);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            }
        }
    }
}
