using RabbitMQ.Client;

namespace Registration.Api.Messaging;

public sealed class RabbitMqClient : IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqClient(IConfiguration configuration)
    {
        _factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMq:Host"] ?? "localhost",
            Port = configuration.GetValue("RabbitMq:Port", 5672),
            UserName = configuration["RabbitMq:Username"]
                ?? throw new InvalidOperationException("RabbitMq:Username is required."),
            Password = configuration["RabbitMq:Password"]
                ?? throw new InvalidOperationException("RabbitMq:Password is required."),
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedHeartbeat = TimeSpan.FromSeconds(30),
            ClientProvidedName = "registration-api"
        };
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
            _connection = await _factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        _connectionGate.Dispose();
    }
}
