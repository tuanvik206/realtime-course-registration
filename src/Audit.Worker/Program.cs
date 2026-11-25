using Audit.Worker.Consumers;
using Audit.Worker.Data;
using Audit.Worker.Endpoints;
using Audit.Worker.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var auditConnection = builder.Configuration.GetConnectionString("AuditDb")
    ?? throw new InvalidOperationException("ConnectionStrings:AuditDb is required.");
builder.Services.AddDbContext<AuditDbContext>(options => options.UseNpgsql(auditConnection));
builder.Services.AddSingleton<RabbitMqClient>();
builder.Services.AddHostedService<AuditConsumerService>();

var app = builder.Build();
await using (var startupScope = app.Services.CreateAsyncScope())
{
    var startupConfig = startupScope.ServiceProvider.GetRequiredService<IConfiguration>();
    var auditDb = startupScope.ServiceProvider.GetRequiredService<AuditDbContext>();
    if (startupConfig.GetValue<bool>("Migrations:ApplyOnStartup"))
    {
        await auditDb.Database.MigrateAsync();
    }
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (
    AuditDbContext dbContext, RabbitMqClient rabbitMq, CancellationToken cancellationToken) =>
{
    try
    {
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        var connection = await rabbitMq.GetConnectionAsync(cancellationToken);
        return connection.IsOpen
            ? Results.Ok(new { status = "ready" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});
app.MapAuditEndpoints();
app.Run();

public partial class Program;
