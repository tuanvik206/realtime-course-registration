using CourseCapacity.Service.Data;
using CourseCapacity.Service.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(8081, listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
});

builder.Services.AddGrpc();
builder.Services.AddDbContext<CapacityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CapacityDb")
        ?? throw new InvalidOperationException("ConnectionStrings:CapacityDb is required.")));
builder.Services.AddScoped<CapacityEnrollmentService>();
builder.Services.AddScoped<CapacityAdminService>();
builder.Services.AddScoped<CapacityDbSeeder>();

var app = builder.Build();
await using (var startupScope = app.Services.CreateAsyncScope())
{
    var startupConfig = startupScope.ServiceProvider.GetRequiredService<IConfiguration>();
    var capacityDb = startupScope.ServiceProvider.GetRequiredService<CapacityDbContext>();
    if (startupConfig.GetValue<bool>("Migrations:ApplyOnStartup"))
    {
        await capacityDb.Database.MigrateAsync();
    }
    if (startupConfig.GetValue<bool>("Seed:Enabled"))
    {
        await startupScope.ServiceProvider.GetRequiredService<CapacityDbSeeder>()
            .SeedAsync();
    }
}

app.MapGrpcService<CapacityGrpcService>();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (CapacityDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();

public partial class Program;
