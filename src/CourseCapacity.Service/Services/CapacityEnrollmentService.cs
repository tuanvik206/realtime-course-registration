using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CourseCapacity.Service.Data;
using CourseCapacity.Service.Data.Entities;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using RealtimeCourseRegistration.Contracts.Common;
using RealtimeCourseRegistration.Contracts.Events;
using CapacityContract = RealtimeCourseRegistration.Contracts.Capacity.V1;

namespace CourseCapacity.Service.Services;

public sealed class CapacityEnrollmentService(CapacityDbContext dbContext)
{
    private const string OperationType = "ReserveSeat";
    private const string ReleaseOperationType = "ReleaseSeat";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CapacityContract.MutationResult> ReserveSeatAsync(
        Guid actorId, Guid studentId, Guid courseSectionId, Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var requestHash = HashRequest(OperationType, studentId, courseSectionId);
        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO processed_operations
                (actor_id, operation_id, operation_type, request_hash, outcome_code, response_json, created_at)
            VALUES ({actorId}, {operationId}, {OperationType}, {requestHash}, 'Pending', 'null'::jsonb, {now})
            ON CONFLICT (actor_id, operation_id) DO NOTHING
            """, cancellationToken);

        if (claimed == 0)
        {
            var previous = await dbContext.ProcessedOperations.AsNoTracking().SingleAsync(
                operation => operation.ActorId == actorId && operation.OperationId == operationId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (previous.OperationType != OperationType || previous.RequestHash != requestHash)
            {
                return Failure(CapacityContract.OperationOutcome.IdempotencyConflict);
            }
            if (previous.OutcomeCode == "Pending")
            {
                throw new InvalidOperationException("A committed operation cannot remain pending.");
            }
            return Deserialize(previous.ResponseJson);
        }

        var section = await dbContext.CourseSections.FromSqlInterpolated($"""
                SELECT * FROM course_sections WHERE id = {courseSectionId} FOR UPDATE
                """)
            .AsTracking().SingleOrDefaultAsync(cancellationToken);
        if (section is null)
        {
            var missing = Failure(CapacityContract.OperationOutcome.CourseNotFound);
            await SaveOutcomeAsync(actorId, operationId, missing, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return missing;
        }

        var existing = await dbContext.Registrations.AsNoTracking().SingleOrDefaultAsync(
            registration => registration.StudentId == studentId
                && registration.CourseSectionId == courseSectionId
                && registration.Status == RegistrationStatus.Active,
            cancellationToken);
        if (existing is not null)
        {
            var duplicate = await MakeSuccessAsync(
                CapacityContract.OperationOutcome.AlreadyRegistered, existing, section.Id, cancellationToken);
            await SaveOutcomeAsync(actorId, operationId, duplicate, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return duplicate;
        }

        if (section.Status != CourseStatus.Open)
        {
            var closed = Failure(CapacityContract.OperationOutcome.CourseClosed, section.Id);
            await SaveOutcomeAsync(actorId, operationId, closed, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return closed;
        }
        if (section.RegisteredCount >= section.Capacity)
        {
            var full = Failure(CapacityContract.OperationOutcome.CourseFull, section.Id);
            await SaveOutcomeAsync(actorId, operationId, full, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return full;
        }

        var registration = new Registration
        {
            Id = Guid.NewGuid(), StudentId = studentId, CourseSectionId = section.Id,
            Status = RegistrationStatus.Active, CreatedAt = now
        };
        dbContext.Registrations.Add(registration);
        section.RegisteredCount++;
        section.Version++;
        section.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var freshSection = await LoadSectionAsync(section.Id, cancellationToken);
        var eventId = Guid.NewGuid();
        var integrationEvent = new IntegrationEvent
        {
            EventId = eventId,
            EventType = IntegrationEventTypes.RegistrationCreated,
            CorrelationId = operationId.ToString(),
            ActorId = actorId,
            RegistrationId = registration.Id,
            StudentId = studentId,
            CourseSectionId = section.Id,
            Capacity = section.Capacity,
            RegisteredCount = section.RegisteredCount,
            RemainingSeats = section.Capacity - section.RegisteredCount,
            Status = section.Status.ToString(),
            Version = section.Version,
            OccurredAt = new DateTimeOffset(now, TimeSpan.Zero)
        };
        var eventValidation = IntegrationEventValidator.Validate(integrationEvent);
        if (!eventValidation.IsValid)
        {
            throw new InvalidOperationException($"Generated outbox event is invalid: {eventValidation.ErrorCode}");
        }
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            EventId = eventId, EventType = integrationEvent.EventType, AggregateId = section.Id,
            Version = section.Version, PayloadJson = IntegrationEventJson.Serialize(integrationEvent),
            OccurredAt = now
        });

        var success = new CapacityContract.MutationResult
        {
            Outcome = CapacityContract.OperationOutcome.Succeeded,
            RegistrationId = registration.Id.ToString(),
            StudentId = studentId.ToString(),
            CourseSectionId = section.Id.ToString(),
            RegistrationStatus = CapacityContract.RegistrationState.Active,
            Snapshot = CapacityGrpcService.CreateSnapshot(freshSection),
            CreatedAt = Timestamp.FromDateTime(now)
        };
        await dbContext.SaveChangesAsync(cancellationToken);
        await SaveOutcomeAsync(actorId, operationId, success, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return success;
    }

    public async Task<CapacityContract.MutationResult> ReleaseSeatAsync(
        Guid actorId, Guid studentId, Guid registrationId, Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var requestHash = HashRequest(ReleaseOperationType, studentId, registrationId);
        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO processed_operations
                (actor_id, operation_id, operation_type, request_hash, outcome_code, response_json, created_at)
            VALUES ({actorId}, {operationId}, {ReleaseOperationType}, {requestHash}, 'Pending', 'null'::jsonb, {now})
            ON CONFLICT (actor_id, operation_id) DO NOTHING
            """, cancellationToken);

        if (claimed == 0)
        {
            var previous = await dbContext.ProcessedOperations.AsNoTracking().SingleAsync(
                operation => operation.ActorId == actorId && operation.OperationId == operationId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (previous.OperationType != ReleaseOperationType || previous.RequestHash != requestHash)
            {
                return Failure(CapacityContract.OperationOutcome.IdempotencyConflict);
            }
            if (previous.OutcomeCode == "Pending")
            {
                throw new InvalidOperationException("A committed operation cannot remain pending.");
            }
            return Deserialize(previous.ResponseJson);
        }

        var registrationRef = await dbContext.Registrations.AsNoTracking()
            .Where(item => item.Id == registrationId)
            .Select(item => new { item.CourseSectionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (registrationRef is null)
        {
            var missing = Failure(CapacityContract.OperationOutcome.RegistrationNotFound);
            await SaveOutcomeAsync(actorId, operationId, missing, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return missing;
        }

        var section = await dbContext.CourseSections.FromSqlInterpolated($"""
                SELECT * FROM course_sections WHERE id = {registrationRef.CourseSectionId} FOR UPDATE
                """)
            .AsTracking().SingleOrDefaultAsync(cancellationToken);
        if (section is null)
        {
            var missing = Failure(CapacityContract.OperationOutcome.CourseNotFound);
            await SaveOutcomeAsync(actorId, operationId, missing, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return missing;
        }

        var registration = await dbContext.Registrations.AsTracking()
            .SingleOrDefaultAsync(item => item.Id == registrationId, cancellationToken);
        if (registration is null || registration.StudentId != studentId)
        {
            var hidden = Failure(CapacityContract.OperationOutcome.RegistrationNotFound);
            await SaveOutcomeAsync(actorId, operationId, hidden, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return hidden;
        }

        if (registration.Status == RegistrationStatus.Cancelled)
        {
            var noOp = await MakeReleaseResultAsync(registration, section.Id, cancellationToken);
            await SaveOutcomeAsync(actorId, operationId, noOp, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return noOp;
        }

        if (section.RegisteredCount <= 0)
        {
            throw new InvalidOperationException("Course RegisteredCount invariant is broken; refusing to decrement below zero.");
        }

        registration.Status = RegistrationStatus.Cancelled;
        registration.CancelledAt = now;
        section.RegisteredCount--;
        section.Version++;
        section.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var freshSection = await LoadSectionAsync(section.Id, cancellationToken);
        var eventId = Guid.NewGuid();
        var integrationEvent = new IntegrationEvent
        {
            EventId = eventId,
            EventType = IntegrationEventTypes.RegistrationCancelled,
            CorrelationId = operationId.ToString(),
            ActorId = actorId,
            RegistrationId = registration.Id,
            StudentId = registration.StudentId,
            CourseSectionId = section.Id,
            Capacity = section.Capacity,
            RegisteredCount = section.RegisteredCount,
            RemainingSeats = section.Capacity - section.RegisteredCount,
            Status = section.Status.ToString(),
            Version = section.Version,
            OccurredAt = new DateTimeOffset(now, TimeSpan.Zero)
        };
        var eventValidation = IntegrationEventValidator.Validate(integrationEvent);
        if (!eventValidation.IsValid)
        {
            throw new InvalidOperationException($"Generated outbox event is invalid: {eventValidation.ErrorCode}");
        }
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            EventId = eventId, EventType = integrationEvent.EventType, AggregateId = section.Id,
            Version = section.Version, PayloadJson = IntegrationEventJson.Serialize(integrationEvent),
            OccurredAt = now
        });

        var success = MakeReleaseResult(registration, freshSection);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SaveOutcomeAsync(actorId, operationId, success, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return success;
    }

    private async Task<CapacityContract.MutationResult> MakeReleaseResultAsync(
        Registration registration, Guid sectionId, CancellationToken cancellationToken) =>
        MakeReleaseResult(registration, await LoadSectionAsync(sectionId, cancellationToken));

    private static CapacityContract.MutationResult MakeReleaseResult(
        Registration registration, CourseSection section) => new()
    {
        Outcome = CapacityContract.OperationOutcome.Succeeded,
        RegistrationId = registration.Id.ToString(),
        StudentId = registration.StudentId.ToString(),
        CourseSectionId = registration.CourseSectionId.ToString(),
        RegistrationStatus = CapacityContract.RegistrationState.Cancelled,
        Snapshot = CapacityGrpcService.CreateSnapshot(section),
        CreatedAt = Timestamp.FromDateTime(registration.CreatedAt)
    };

    private async Task<CourseSection> LoadSectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        await dbContext.CourseSections.AsNoTracking()
            .Include(section => section.Subject).Include(section => section.Semester).Include(section => section.Schedules)
            .SingleAsync(section => section.Id == sectionId, cancellationToken);

    private async Task<CapacityContract.MutationResult> MakeSuccessAsync(
        CapacityContract.OperationOutcome outcome, Registration registration, Guid sectionId,
        CancellationToken cancellationToken)
    {
        var section = await LoadSectionAsync(sectionId, cancellationToken);
        return new CapacityContract.MutationResult
        {
            Outcome = outcome,
            RegistrationId = registration.Id.ToString(),
            StudentId = registration.StudentId.ToString(),
            CourseSectionId = registration.CourseSectionId.ToString(),
            RegistrationStatus = CapacityContract.RegistrationState.Active,
            Snapshot = CapacityGrpcService.CreateSnapshot(section),
            CreatedAt = Timestamp.FromDateTime(registration.CreatedAt)
        };
    }

    private async Task SaveOutcomeAsync(Guid actorId, Guid operationId,
        CapacityContract.MutationResult result, CancellationToken cancellationToken)
    {
        var responseJson = JsonSerializer.Serialize(StoredResult.From(result), JsonOptions);
        var outcome = result.Outcome.ToString();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE processed_operations
            SET outcome_code = {outcome}, response_json = CAST({responseJson} AS jsonb)
            WHERE actor_id = {actorId} AND operation_id = {operationId}
            """, cancellationToken);
    }

    private static CapacityContract.MutationResult Deserialize(string json)
    {
        var saved = JsonSerializer.Deserialize<StoredResult>(json, JsonOptions)
            ?? throw new InvalidOperationException("Stored idempotency result is invalid.");
        if (!System.Enum.TryParse(saved.Outcome, out CapacityContract.OperationOutcome outcome))
        {
            throw new InvalidOperationException("Stored idempotency outcome is invalid.");
        }
        var result = new CapacityContract.MutationResult
        {
            Outcome = outcome, ErrorCode = saved.ErrorCode ?? "", Message = saved.Message ?? "",
            RegistrationId = saved.RegistrationId ?? "", StudentId = saved.StudentId ?? "",
            CourseSectionId = saved.CourseSectionId ?? "",
            RegistrationStatus = (CapacityContract.RegistrationState)saved.RegistrationStatus
        };
        if (saved.CreatedAt.HasValue)
        {
            result.CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(saved.CreatedAt.Value, DateTimeKind.Utc));
        }
        if (saved.Snapshot is not null)
        {
            result.Snapshot = saved.Snapshot.ToContract();
        }
        return result;
    }

    private static CapacityContract.MutationResult Failure(
        CapacityContract.OperationOutcome outcome, Guid? courseSectionId = null) => new()
    {
        Outcome = outcome,
        ErrorCode = OutcomeErrorCodeMap.ToErrorCode(outcome) ?? "",
        CourseSectionId = courseSectionId?.ToString() ?? ""
    };

    private static string HashRequest(string operationType, params Guid[] values)
    {
        var canonical = string.Join('|', new[] { operationType }.Concat(values.Select(value => value.ToString("D"))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed record StoredResult(
        string Outcome, string? ErrorCode, string? Message, string? RegistrationId,
        string? StudentId, string? CourseSectionId, int RegistrationStatus,
        DateTime? CreatedAt, StoredCourse? Snapshot)
    {
        public static StoredResult From(CapacityContract.MutationResult result) => new(
            result.Outcome.ToString(), result.ErrorCode, result.Message, result.RegistrationId,
            result.StudentId, result.CourseSectionId, (int)result.RegistrationStatus,
            result.CreatedAt?.ToDateTime(), result.Snapshot is null ? null : StoredCourse.From(result.Snapshot));
    }

    private sealed record StoredSchedule(int DayOfWeek, string StartTime, string EndTime, string Room);

    private sealed record StoredCourse(
        string CourseSectionId, string CourseCode, string SubjectId, string SubjectCode, string SubjectName,
        int Credits, string SemesterId, string SemesterName, string LecturerName, int Capacity,
        int RegisteredCount, int RemainingSeats, int Status, long Version, StoredSchedule[] Schedules)
    {
        public static StoredCourse From(CapacityContract.CourseSnapshot source) => new(
            source.CourseSectionId, source.CourseCode, source.SubjectId, source.SubjectCode, source.SubjectName,
            source.Credits, source.SemesterId, source.SemesterName, source.LecturerName, source.Capacity,
            source.RegisteredCount, source.RemainingSeats, (int)source.Status, source.Version,
            source.Schedules.Select(item => new StoredSchedule(item.DayOfWeek, item.StartTime, item.EndTime, item.Room)).ToArray());

        public CapacityContract.CourseSnapshot ToContract()
        {
            var result = new CapacityContract.CourseSnapshot
            {
                CourseSectionId = CourseSectionId, CourseCode = CourseCode, SubjectId = SubjectId,
                SubjectCode = SubjectCode, SubjectName = SubjectName, Credits = Credits,
                SemesterId = SemesterId, SemesterName = SemesterName, LecturerName = LecturerName,
                Capacity = Capacity, RegisteredCount = RegisteredCount, RemainingSeats = RemainingSeats,
                Status = (CapacityContract.CourseStatus)Status, Version = Version
            };
            result.Schedules.AddRange(Schedules.Select(item => new CapacityContract.CourseSchedule
            {
                DayOfWeek = item.DayOfWeek, StartTime = item.StartTime, EndTime = item.EndTime, Room = item.Room
            }));
            return result;
        }
    }
}
