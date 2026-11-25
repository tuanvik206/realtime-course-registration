using RabbitMQ.Client;
using RealtimeCourseRegistration.Contracts.Events;

namespace Registration.Api.Messaging;

public static class RabbitMqTopology
{
    public static async Task DeclareAsync(IConnection connection, CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(BrokerTopology.Exchange, ExchangeType.Topic, durable: true,
            autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(BrokerTopology.DeadLetterExchange, ExchangeType.Direct, durable: true,
            autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(BrokerTopology.NotificationDeadLetterQueue, durable: true,
            exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(BrokerTopology.AuditDeadLetterQueue, durable: true,
            exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(BrokerTopology.NotificationDeadLetterQueue,
            BrokerTopology.DeadLetterExchange, BrokerTopology.NotificationDeadLetterQueue,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(BrokerTopology.AuditDeadLetterQueue,
            BrokerTopology.DeadLetterExchange, BrokerTopology.AuditDeadLetterQueue,
            cancellationToken: cancellationToken);

        await DeclareConsumerQueueAsync(channel, BrokerTopology.NotificationQueue,
            BrokerTopology.NotificationDeadLetterQueue, cancellationToken);
        await DeclareConsumerQueueAsync(channel, BrokerTopology.AuditQueue,
            BrokerTopology.AuditDeadLetterQueue, cancellationToken);
    }

    private static async Task DeclareConsumerQueueAsync(
        IChannel channel, string queueName, string deadLetterQueue, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = BrokerTopology.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = deadLetterQueue
        };
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false,
            arguments: arguments, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queueName, BrokerTopology.Exchange, BrokerTopology.RegistrationBinding,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queueName, BrokerTopology.Exchange, BrokerTopology.CourseBinding,
            cancellationToken: cancellationToken);
    }
}
