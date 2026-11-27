using System.Threading.RateLimiting;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Registration.Api.Authentication;
using Registration.Api.Data;
using Registration.Api.Data.Entities;
using Registration.Api.Endpoints;
using Registration.Api.Middleware;
using Registration.Api.Messaging;
using RealtimeCourseRegistration.Contracts.Capacity.V1;

var builder = WebApplication.CreateBuilder(args);

var jwtTokenService = new JwtTokenService(builder.Configuration);
var identityConnection = builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("ConnectionStrings:IdentityDb is required.");
var capacityAddress = builder.Configuration["Grpc:CapacityAddress"] ?? "http://localhost:8080";

builder.Services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(identityConnection));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IdentityDbSeeder>();
builder.Services.AddSingleton(jwtTokenService);
builder.Services.AddGrpcClient<CourseCapacity.CourseCapacityClient>(options =>
    options.Address = new Uri(capacityAddress));
builder.Services.AddHttpClient("audit", client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<RabbitMqClient>();
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = jwtTokenService.CreateValidationParameters();
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token)
                    && context.Request.Cookies.TryGetValue("courseflow.auth", out var cookieToken))
                {
                    context.Token = cookieToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Student", policy => policy.RequireAuthenticatedUser().RequireRole("Student"));
    options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("mutations", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();
await using (var startupScope = app.Services.CreateAsyncScope())
{
    var startupConfig = startupScope.ServiceProvider.GetRequiredService<IConfiguration>();
    var identityDb = startupScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    if (startupConfig.GetValue<bool>("Migrations:ApplyOnStartup"))
    {
        await identityDb.Database.MigrateAsync();
    }
    if (startupConfig.GetValue<bool>("Seed:Enabled"))
    {
        var adminPassword = startupConfig["Seed:AdminPassword"]
            ?? throw new InvalidOperationException("Seed:AdminPassword is required when seeding is enabled.");
        var studentPassword = startupConfig["Seed:StudentPassword"]
            ?? throw new InvalidOperationException("Seed:StudentPassword is required when seeding is enabled.");
        await startupScope.ServiceProvider.GetRequiredService<IdentityDbSeeder>()
            .SeedAsync(adminPassword, studentPassword);
    }
}
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Registration.Api.Unhandled");
    logger.LogError("Unhandled API exception. CorrelationId={CorrelationId}",
        CorrelationIdMiddleware.Get(context));
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ApiProblemResults.Create(context, 500, "INTERNAL_ERROR", "Yêu cầu không thể hoàn tất.")
        .ExecuteAsync(context);
}));
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (
    IdentityDbContext dbContext,
    CourseCapacity.CourseCapacityClient capacity,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        await capacity.ListSemestersAsync(new ListSemestersRequest(),
            new Grpc.Core.Metadata { { "x-correlation-id", CorrelationIdMiddleware.Get(context).ToString() } },
            DateTime.UtcNow.AddSeconds(2), cancellationToken);
        return Results.Ok(new { status = "ready" });
    }
    catch
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapAuthEndpoints();
app.MapCourseEndpoints();
app.MapRegistrationEndpoints();
app.MapAdminEndpoints();
app.Run();

public partial class Program;
