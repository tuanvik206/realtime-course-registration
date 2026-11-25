using System.Security.Cryptography;
using System.Text;
using Audit.Worker.Data;
using Audit.Worker.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Audit.Worker.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/internal/audit-logs", GetAuditLogsAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAuditLogsAsync(
        Guid? courseSectionId,
        Guid? studentId,
        string? eventType,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        HttpContext context,
        AuditDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(context, configuration["Audit:InternalApiKey"]))
        {
            return Results.Unauthorized();
        }
        if (page < 1 || pageSize < 1 || pageSize > 100
            || (from.HasValue && to.HasValue && from > to)
            || eventType?.Length > 64
            || courseSectionId == Guid.Empty || studentId == Guid.Empty)
        {
            return Results.Problem(statusCode: 400, title: "Invalid audit filter.");
        }

        var query = dbContext.AuditLogs.AsNoTracking();
        if (courseSectionId.HasValue)
        {
            query = query.Where(log => log.CourseSectionId == courseSectionId.Value);
        }
        if (studentId.HasValue)
        {
            query = query.Where(log => log.StudentId == studentId.Value);
        }
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(log => log.EventType == eventType);
        }
        if (from.HasValue)
        {
            query = query.Where(log => log.OccurredAt >= from.Value.UtcDateTime);
        }
        if (to.HasValue)
        {
            query = query.Where(log => log.OccurredAt <= to.Value.UtcDateTime);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var records = await query.OrderByDescending(log => log.OccurredAt).ThenBy(log => log.EventId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Results.Ok(new PagedResult<AuditLogDto>(
            records.Select(ToDto).ToArray(), page, pageSize, totalCount));
    }

    private static bool IsAuthorized(HttpContext context, string? expectedKey)
    {
        var actualKey = context.Request.Headers["X-Audit-Internal-Key"].ToString();
        if (string.IsNullOrWhiteSpace(expectedKey) || string.IsNullOrWhiteSpace(actualKey))
        {
            return false;
        }
        var expected = Encoding.UTF8.GetBytes(expectedKey);
        var actual = Encoding.UTF8.GetBytes(actualKey);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static AuditLogDto ToDto(AuditLog log) => new(
        log.EventId, log.EventType, log.SchemaVersion, log.CorrelationId, log.ActorId, log.StudentId,
        log.RegistrationId, log.CourseSectionId, log.Capacity, log.RegisteredCount, log.RemainingSeats,
        log.Status, log.Version, new DateTimeOffset(log.OccurredAt, TimeSpan.Zero),
        new DateTimeOffset(log.ReceivedAt, TimeSpan.Zero));
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);
public sealed record AuditLogDto(
    Guid EventId, string EventType, int SchemaVersion, string CorrelationId, Guid? ActorId,
    Guid? StudentId, Guid? RegistrationId, Guid CourseSectionId, int Capacity, int RegisteredCount,
    int RemainingSeats, string Status, long Version, DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt);
