using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using CourseCapacity.Service.Data;
using CourseCapacity.Service.Data.Entities;
using RealtimeCourseRegistration.Contracts.Events;
using CapacityContract = RealtimeCourseRegistration.Contracts.Capacity.V1;

namespace CourseCapacity.Service.Services;

public sealed class CapacityAdminService(CapacityDbContext dbContext)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<CapacityContract.SubjectMutationResult> CreateSubjectAsync(
        CapacityContract.CreateSubjectRequest request, CancellationToken cancellationToken)
    {
        var actorId = ParseUuid(request.ActorId, "actor_id");
        var operationId = ParseUuid(request.OperationId, "operation_id");
        var (code, name) = ValidateSubject(request.Code, request.Name, request.Credits);
        const string operationType = "CreateSubject";
        var requestHash = Hash(operationType, code, name, request.Credits.ToString(Invariant));
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var previous = await ClaimAsync(actorId, operationId, operationType, requestHash, cancellationToken);
        if (previous is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return previous.OperationType != operationType || previous.RequestHash != requestHash
                ? SubjectFailure(CapacityContract.OperationOutcome.IdempotencyConflict, "IDEMPOTENCY_CONFLICT")
                : JsonParser.Default.Parse<CapacityContract.SubjectMutationResult>(previous.ResponseJson);
        }
        var subject = new Subject
        {
            Id = Guid.NewGuid(), Code = code, Name = name, Credits = request.Credits,
            IsActive = true, Version = 1
        };
        dbContext.Subjects.Add(subject);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return SubjectFailure(CapacityContract.OperationOutcome.ValidationError, "SUBJECT_CODE_EXISTS");
        }
        var result = SubjectResult(subject);
        await SaveOutcomeAsync(actorId, operationId, result, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CapacityContract.SubjectMutationResult> UpdateSubjectAsync(
        CapacityContract.UpdateSubjectRequest request, CancellationToken cancellationToken)
    {
        var actorId = ParseUuid(request.ActorId, "actor_id");
        var subjectId = ParseUuid(request.SubjectId, "subject_id");
        var operationId = ParseUuid(request.OperationId, "operation_id");
        var (code, name) = ValidateSubject(request.Code, request.Name, request.Credits);
        var operationType = "UpdateSubject";
        var hash = Hash(operationType, subjectId.ToString("D"), code, name,
            request.Credits.ToString(Invariant), request.IsActive.ToString(), request.ExpectedVersion.ToString(Invariant));
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var previous = await ClaimAsync(actorId, operationId, operationType, hash, cancellationToken);
        if (previous is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return previous.OperationType != operationType || previous.RequestHash != hash
                ? SubjectFailure(CapacityContract.OperationOutcome.IdempotencyConflict, "IDEMPOTENCY_CONFLICT")
                : JsonParser.Default.Parse<CapacityContract.SubjectMutationResult>(previous.ResponseJson);
        }

        var subject = await dbContext.Subjects.FromSqlInterpolated($"SELECT * FROM subjects WHERE id = {subjectId} FOR UPDATE")
            .AsTracking().SingleOrDefaultAsync(cancellationToken);
        if (subject is null)
        {
            var missing = SubjectFailure(CapacityContract.OperationOutcome.CourseNotFound, "SUBJECT_NOT_FOUND");
            await SaveOutcomeAsync(actorId, operationId, missing, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return missing;
        }
        if (request.ExpectedVersion != subject.Version)
        {
            var conflict = SubjectFailure(CapacityContract.OperationOutcome.VersionConflict, "VERSION_CONFLICT");
            conflict.Subject = ToSubject(subject);
            await SaveOutcomeAsync(actorId, operationId, conflict, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return conflict;
        }

        subject.Code = code;
        subject.Name = name;
        subject.Credits = request.Credits;
        subject.IsActive = request.IsActive;
        subject.Version++;
        var now = DateTime.UtcNow;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw AlreadyExists("SUBJECT_CODE_EXISTS");
        }
        var affectedCourses = await dbContext.CourseSections.FromSqlInterpolated($"""
                SELECT * FROM course_sections WHERE subject_id = {subjectId} ORDER BY id FOR UPDATE
                """)
            .AsTracking().ToListAsync(cancellationToken);
        foreach (var course in affectedCourses)
        {
            course.Version++;
            course.UpdatedAt = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var course in affectedCourses)
        {
            var snapshot = await LoadCourseAsync(course.Id, cancellationToken);
            await AddEventAsync(IntegrationEventTypes.CourseUpdated, actorId, operationId, snapshot, now, cancellationToken);
        }
        var result = SubjectResult(subject);
        await SaveOutcomeAsync(actorId, operationId, result, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CapacityContract.CourseMutationResult> CreateCourseAsync(
        CapacityContract.CreateCourseRequest request, CancellationToken cancellationToken)
    {
        var actorId = ParseUuid(request.ActorId, "actor_id");
        var operationId = ParseUuid(request.OperationId, "operation_id");
        var input = ValidateCourse(request.Course, requireId: true);
        var hash = Hash("CreateCourse", CourseCanonical(input));
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var previous = await ClaimAsync(actorId, operationId, "CreateCourse", hash, cancellationToken);
        if (previous is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return previous.OperationType != "CreateCourse" || previous.RequestHash != hash
                ? CourseFailure(CapacityContract.OperationOutcome.IdempotencyConflict, "IDEMPOTENCY_CONFLICT")
                : JsonParser.Default.Parse<CapacityContract.CourseMutationResult>(previous.ResponseJson);
        }

        var subject = await dbContext.Subjects.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == input.SubjectId && item.IsActive, cancellationToken);
        var semester = await dbContext.Semesters.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == input.SemesterId && item.IsActive, cancellationToken);
        if (subject is null || semester is null)
        {
            var invalid = CourseFailure(CapacityContract.OperationOutcome.ValidationError,
                subject is null ? "SUBJECT_NOT_FOUND" : "SEMESTER_NOT_FOUND");
            await SaveOutcomeAsync(actorId, operationId, invalid, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return invalid;
        }

        var now = DateTime.UtcNow;
        var section = new CourseSection
        {
            Id = input.Id, CourseCode = input.Code, SubjectId = input.SubjectId, SemesterId = input.SemesterId,
            LecturerName = input.LecturerName, Capacity = input.Capacity, RegisteredCount = 0,
            Status = CourseStatus.Open, Version = 1, CreatedAt = now, UpdatedAt = now,
            Schedules = input.Schedules.Select(ToSchedule).ToArray()
        };
        dbContext.CourseSections.Add(section);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            var conflict = CourseFailure(CapacityContract.OperationOutcome.ValidationError, "COURSE_CODE_EXISTS");
            return conflict;
        }

        var loaded = await LoadCourseAsync(section.Id, cancellationToken);
        await AddEventAsync(IntegrationEventTypes.CourseCreated, actorId, operationId, loaded, now, cancellationToken);
        var result = new CapacityContract.CourseMutationResult
        {
            Outcome = CapacityContract.OperationOutcome.Succeeded,
            Course = CapacityGrpcService.CreateSnapshot(loaded)
        };
        await SaveOutcomeAsync(actorId, operationId, result, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CapacityContract.CourseMutationResult> UpdateCourseAsync(
        CapacityContract.UpdateCourseRequest request, CancellationToken cancellationToken)
    {
        var actorId = ParseUuid(request.ActorId, "actor_id");
        var operationId = ParseUuid(request.OperationId, "operation_id");
        var input = ValidateCourse(request.Course, requireId: true);
        var hash = Hash("UpdateCourse", CourseCanonical(input), request.ExpectedVersion.ToString(Invariant));
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var previous = await ClaimAsync(actorId, operationId, "UpdateCourse", hash, cancellationToken);
        if (previous is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return previous.OperationType != "UpdateCourse" || previous.RequestHash != hash
                ? CourseFailure(CapacityContract.OperationOutcome.IdempotencyConflict, "IDEMPOTENCY_CONFLICT")
                : JsonParser.Default.Parse<CapacityContract.CourseMutationResult>(previous.ResponseJson);
        }

        var section = await dbContext.CourseSections.FromSqlInterpolated(
                $"SELECT * FROM course_sections WHERE id = {input.Id} FOR UPDATE")
            .AsTracking().Include(item => item.Schedules).SingleOrDefaultAsync(cancellationToken);
        if (section is null)
        {
            var missing = CourseFailure(CapacityContract.OperationOutcome.CourseNotFound, "COURSE_NOT_FOUND");
            await SaveOutcomeAsync(actorId, operationId, missing, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return missing;
        }
        if (section.Version != request.ExpectedVersion)
        {
            var conflict = CourseFailure(CapacityContract.OperationOutcome.VersionConflict, "VERSION_CONFLICT");
            conflict.Course = CapacityGrpcService.CreateSnapshot(await LoadCourseAsync(section.Id, cancellationToken));
            await SaveOutcomeAsync(actorId, operationId, conflict, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return conflict;
        }
        if (input.Capacity < section.RegisteredCount)
        {
            var invalidCapacity = CourseFailure(CapacityContract.OperationOutcome.InvalidCapacity, "INVALID_CAPACITY");
            invalidCapacity.Course = CapacityGrpcService.CreateSnapshot(await LoadCourseAsync(section.Id, cancellationToken));
            await SaveOutcomeAsync(actorId, operationId, invalidCapacity, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return invalidCapacity;
        }
        if ((input.SubjectId != section.SubjectId || input.SemesterId != section.SemesterId)
            && await dbContext.Registrations.AnyAsync(item => item.CourseSectionId == section.Id, cancellationToken))
        {
            var conflict = CourseFailure(CapacityContract.OperationOutcome.ValidationError, "COURSE_HAS_REGISTRATIONS");
            conflict.Course = CapacityGrpcService.CreateSnapshot(await LoadCourseAsync(section.Id, cancellationToken));
            await SaveOutcomeAsync(actorId, operationId, conflict, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return conflict;
        }

        var referencesValid = await dbContext.Subjects.AnyAsync(item => item.Id == input.SubjectId && item.IsActive, cancellationToken)
            && await dbContext.Semesters.AnyAsync(item => item.Id == input.SemesterId && item.IsActive, cancellationToken);
        if (!referencesValid)
        {
            var invalid = CourseFailure(CapacityContract.OperationOutcome.ValidationError, "SUBJECT_OR_SEMESTER_NOT_FOUND");
            await SaveOutcomeAsync(actorId, operationId, invalid, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return invalid;
        }

        var now = DateTime.UtcNow;
        section.CourseCode = input.Code;
        section.SubjectId = input.SubjectId;
        section.SemesterId = input.SemesterId;
        section.LecturerName = input.LecturerName;
        section.Capacity = input.Capacity;
        section.Version++;
        section.UpdatedAt = now;
        dbContext.CourseSchedules.RemoveRange(section.Schedules);
        section.Schedules = input.Schedules.Select(ToSchedule).ToArray();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            var duplicate = CourseFailure(CapacityContract.OperationOutcome.ValidationError, "COURSE_CODE_EXISTS");
            return duplicate;
        }

        var updated = await LoadCourseAsync(section.Id, cancellationToken);
        await AddEventAsync(IntegrationEventTypes.CourseUpdated, actorId, operationId, updated, now, cancellationToken);
        var result = new CapacityContract.CourseMutationResult
        {
            Outcome = CapacityContract.OperationOutcome.Succeeded,
            Course = CapacityGrpcService.CreateSnapshot(updated)
        };
        await SaveOutcomeAsync(actorId, operationId, result, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task<CapacityContract.MutationResult> UpdateCapacityAsync(
        CapacityContract.UpdateCapacityRequest request, CancellationToken cancellationToken) =>
        UpdateCourseStateAsync(ParseUuid(request.ActorId, "actor_id"), ParseUuid(request.OperationId, "operation_id"),
            ParseUuid(request.CourseSectionId, "course_section_id"), request.ExpectedVersion,
            request.Capacity, null, cancellationToken);

    public Task<CapacityContract.MutationResult> UpdateStatusAsync(
        CapacityContract.UpdateStatusRequest request, CancellationToken cancellationToken)
    {
        var status = request.Status switch
        {
            CapacityContract.CourseStatus.Open => CourseStatus.Open,
            CapacityContract.CourseStatus.Closed => CourseStatus.Closed,
            _ => throw InvalidArgument("status must be Open or Closed.")
        };
        return UpdateCourseStateAsync(ParseUuid(request.ActorId, "actor_id"), ParseUuid(request.OperationId, "operation_id"),
            ParseUuid(request.CourseSectionId, "course_section_id"), request.ExpectedVersion,
            null, status, cancellationToken);
    }

    private async Task<CapacityContract.MutationResult> UpdateCourseStateAsync(
        Guid actorId, Guid operationId, Guid courseId, long expectedVersion,
        int? capacity, CourseStatus? status, CancellationToken cancellationToken)
    {
        var operationType = capacity.HasValue ? "UpdateCapacity" : "UpdateStatus";
        var hash = Hash(operationType, courseId.ToString("D"), capacity?.ToString(Invariant) ?? status!.Value.ToString(),
            expectedVersion.ToString(Invariant));
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var previous = await ClaimAsync(actorId, operationId, operationType, hash, cancellationToken);
        if (previous is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return previous.OperationType != operationType || previous.RequestHash != hash
                ? MutationFailure(CapacityContract.OperationOutcome.IdempotencyConflict, "IDEMPOTENCY_CONFLICT")
                : JsonParser.Default.Parse<CapacityContract.MutationResult>(previous.ResponseJson);
        }

        var section = await dbContext.CourseSections.FromSqlInterpolated(
                $"SELECT * FROM course_sections WHERE id = {courseId} FOR UPDATE")
            .AsTracking().SingleOrDefaultAsync(cancellationToken);
        if (section is null)
        {
            var missing = MutationFailure(CapacityContract.OperationOutcome.CourseNotFound, "COURSE_NOT_FOUND");
            await SaveOutcomeAsync(actorId, operationId, missing, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return missing;
        }

        CapacityContract.MutationResult result;
        if (section.Version != expectedVersion)
        {
            result = MutationFailure(CapacityContract.OperationOutcome.VersionConflict, "VERSION_CONFLICT");
        }
        else if (capacity.HasValue && (capacity.Value <= 0 || capacity.Value < section.RegisteredCount))
        {
            result = MutationFailure(CapacityContract.OperationOutcome.InvalidCapacity, "INVALID_CAPACITY");
        }
        else
        {
            var now = DateTime.UtcNow;
            if (capacity.HasValue) section.Capacity = capacity.Value;
            if (status.HasValue) section.Status = status.Value;
            section.Version++;
            section.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            var loaded = await LoadCourseAsync(section.Id, cancellationToken);
            var eventType = capacity.HasValue
                ? IntegrationEventTypes.CourseCapacityChanged
                : IntegrationEventTypes.CourseStatusChanged;
            await AddEventAsync(eventType, actorId, operationId, loaded, now, cancellationToken);
            result = new CapacityContract.MutationResult
            {
                Outcome = CapacityContract.OperationOutcome.Succeeded,
                CourseSectionId = section.Id.ToString(),
                Snapshot = CapacityGrpcService.CreateSnapshot(loaded)
            };
        }
        if (result.Snapshot is null)
        {
            result.CourseSectionId = section.Id.ToString();
            result.Snapshot = CapacityGrpcService.CreateSnapshot(await LoadCourseAsync(section.Id, cancellationToken));
        }
        await SaveOutcomeAsync(actorId, operationId, result, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CapacityContract.RegistrationPage> GetCourseRegistrationsAsync(
        CapacityContract.CourseRegistrationsRequest request, CancellationToken cancellationToken)
    {
        var courseId = ParseUuid(request.CourseSectionId, "course_section_id");
        var (page, pageSize) = ValidatePage(request.Page, request.PageSize);
        var query = dbContext.Registrations.AsNoTracking()
            .Include(item => item.CourseSection).ThenInclude(section => section.Subject)
            .Include(item => item.CourseSection).ThenInclude(section => section.Semester)
            .Include(item => item.CourseSection).ThenInclude(section => section.Schedules)
            .Where(item => item.CourseSectionId == courseId);
        var total = await query.LongCountAsync(cancellationToken);
        var registrations = await query.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var response = new CapacityContract.RegistrationPage { Page = page, PageSize = pageSize, TotalCount = total };
        response.Registrations.AddRange(registrations.Select(item => new CapacityContract.RegistrationRecord
        {
            RegistrationId = item.Id.ToString(), StudentId = item.StudentId.ToString(),
            CourseSectionId = item.CourseSectionId.ToString(),
            Status = item.Status == RegistrationStatus.Active
                ? CapacityContract.RegistrationState.Active : CapacityContract.RegistrationState.Cancelled,
            CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(item.CreatedAt),
            CancelledAt = item.CancelledAt.HasValue
                ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(item.CancelledAt.Value)
                : null,
            Course = CapacityGrpcService.CreateSnapshot(item.CourseSection)
        }));
        return response;
    }

    public async Task<CapacityContract.StatisticsResponse> GetStatisticsAsync(
        CapacityContract.StatisticsRequest request, CancellationToken cancellationToken)
    {
        var courses = dbContext.CourseSections.AsNoTracking().AsQueryable();
        var registrations = dbContext.Registrations.AsNoTracking()
            .Where(item => item.Status == RegistrationStatus.Active).AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.SemesterId))
        {
            var semesterId = ParseUuid(request.SemesterId, "semester_id");
            courses = courses.Where(item => item.SemesterId == semesterId);
            registrations = registrations.Where(item => item.CourseSection.SemesterId == semesterId);
        }
        return new CapacityContract.StatisticsResponse
        {
            StudentCount = await dbContext.Registrations.AsNoTracking().Select(item => item.StudentId).Distinct().LongCountAsync(cancellationToken),
            CourseCount = await courses.LongCountAsync(cancellationToken),
            ActiveRegistrationCount = await registrations.LongCountAsync(cancellationToken),
            FullCourseCount = await courses.LongCountAsync(item => item.RegisteredCount >= item.Capacity, cancellationToken)
        };
    }

    private async Task<ProcessedOperation?> ClaimAsync(
        Guid actorId, Guid operationId, string operationType, string requestHash, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var claimed = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO processed_operations
                (actor_id, operation_id, operation_type, request_hash, outcome_code, response_json, created_at)
            VALUES ({actorId}, {operationId}, {operationType}, {requestHash}, 'Pending', 'null'::jsonb, {now})
            ON CONFLICT (actor_id, operation_id) DO NOTHING
            """, cancellationToken);
        return claimed == 1 ? null : await dbContext.ProcessedOperations.AsNoTracking()
            .SingleAsync(item => item.ActorId == actorId && item.OperationId == operationId, cancellationToken);
    }

    private async Task SaveOutcomeAsync<T>(Guid actorId, Guid operationId, T response, CancellationToken cancellationToken)
        where T : IMessage<T>
    {
        var json = JsonFormatter.Default.Format(response);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE processed_operations
            SET outcome_code = 'Completed', response_json = CAST({json} AS jsonb)
            WHERE actor_id = {actorId} AND operation_id = {operationId}
            """, cancellationToken);
    }

    private async Task AddEventAsync(string type, Guid actorId, Guid operationId, CourseSection section,
        DateTime now, CancellationToken cancellationToken)
    {
        var message = new IntegrationEvent
        {
            EventId = Guid.NewGuid(), EventType = type, CorrelationId = operationId.ToString(), ActorId = actorId,
            CourseSectionId = section.Id, Capacity = section.Capacity, RegisteredCount = section.RegisteredCount,
            RemainingSeats = section.Capacity - section.RegisteredCount, Status = section.Status.ToString(),
            Version = section.Version, OccurredAt = new DateTimeOffset(now, TimeSpan.Zero)
        };
        var validation = IntegrationEventValidator.Validate(message);
        if (!validation.IsValid) throw new InvalidOperationException($"Invalid admin event: {validation.ErrorCode}");
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            EventId = message.EventId, EventType = type, AggregateId = section.Id, Version = section.Version,
            PayloadJson = IntegrationEventJson.Serialize(message), OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<CourseSection> LoadCourseAsync(Guid id, CancellationToken cancellationToken) => dbContext.CourseSections.AsNoTracking()
        .Include(item => item.Subject).Include(item => item.Semester).Include(item => item.Schedules)
        .SingleAsync(item => item.Id == id, cancellationToken);

    private static CapacityContract.SubjectMutationResult SubjectResult(Subject subject) => new()
    {
        Outcome = CapacityContract.OperationOutcome.Succeeded, Subject = ToSubject(subject)
    };

    private static CapacityContract.SubjectSnapshot ToSubject(Subject subject) => new()
    {
        SubjectId = subject.Id.ToString(), Code = subject.Code, Name = subject.Name,
        Credits = subject.Credits, IsActive = subject.IsActive, Version = subject.Version
    };

    private static CapacityContract.SubjectMutationResult SubjectFailure(CapacityContract.OperationOutcome outcome, string code) =>
        new() { Outcome = outcome, ErrorCode = code };

    private static CapacityContract.CourseMutationResult CourseFailure(CapacityContract.OperationOutcome outcome, string code) =>
        new() { Outcome = outcome, ErrorCode = code };

    private static CapacityContract.MutationResult MutationFailure(CapacityContract.OperationOutcome outcome, string code) =>
        new() { Outcome = outcome, ErrorCode = code };

    private static (string Code, string Name) ValidateSubject(string code, string name, int credits)
    {
        code = code.Trim().ToUpperInvariant();
        name = name.Trim();
        if (code.Length is < 2 or > 32 || name.Length is < 2 or > 160 || credits is < 1 or > 30)
            throw InvalidArgument("Subject code/name/credits are outside allowed bounds.");
        return (code, name);
    }

    private static CourseInput ValidateCourse(CapacityContract.CourseMutationInput? course, bool requireId)
    {
        if (course is null) throw InvalidArgument("course is required.");
        var id = string.IsNullOrWhiteSpace(course.CourseSectionId)
            ? (requireId ? Guid.Empty : Guid.NewGuid())
            : ParseUuid(course.CourseSectionId, "course_section_id");
        var subjectId = ParseUuid(course.SubjectId, "subject_id");
        var semesterId = ParseUuid(course.SemesterId, "semester_id");
        var code = course.CourseCode.Trim().ToUpperInvariant();
        var lecturer = course.LecturerName.Trim();
        if (id == Guid.Empty || code.Length is < 2 or > 48 || lecturer.Length is < 2 or > 160
            || course.Capacity < 1 || course.Schedules.Count > 20)
            throw InvalidArgument("Course fields are outside allowed bounds.");
        var schedules = course.Schedules.Select(schedule =>
        {
            if (schedule.DayOfWeek is < 1 or > 7 || schedule.Room.Trim().Length is < 1 or > 64
                || !TimeOnly.TryParse(schedule.StartTime, Invariant, out var start)
                || !TimeOnly.TryParse(schedule.EndTime, Invariant, out var end) || start >= end)
                throw InvalidArgument("Course schedule is invalid.");
            return new ScheduleInput(schedule.DayOfWeek, start, end, schedule.Room.Trim());
        }).ToArray();
        if (schedules.Distinct().Count() != schedules.Length)
            throw InvalidArgument("Duplicate course schedule entries are not allowed.");
        return new CourseInput(id, subjectId, semesterId, code, lecturer, course.Capacity, schedules);
    }

    private static string CourseCanonical(CourseInput course) => JsonSerializer.Serialize(new
    {
        id = course.Id,
        subjectId = course.SubjectId,
        semesterId = course.SemesterId,
        code = course.Code,
        course.LecturerName,
        course.Capacity,
        schedules = course.Schedules.OrderBy(item => item.Day).ThenBy(item => item.Start)
            .Select(item => new { item.Day, start = item.Start.ToString("HH:mm:ss", Invariant), end = item.End.ToString("HH:mm:ss", Invariant), item.Room })
    });

    private static CourseSchedule ToSchedule(ScheduleInput item) => new()
    {
        Id = Guid.NewGuid(), DayOfWeek = item.Day, StartTime = item.Start, EndTime = item.End, Room = item.Room
    };

    private static string Hash(params string[] parts) => Convert.ToHexString(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(parts)));

    private static Guid ParseUuid(string value, string field)
    {
        if (!Guid.TryParse(value, out var result) || result == Guid.Empty)
            throw InvalidArgument($"{field} must be a non-empty UUID.");
        return result;
    }

    private static (int Page, int PageSize) ValidatePage(int page, int pageSize)
    {
        page = page == 0 ? 1 : page;
        pageSize = pageSize == 0 ? 20 : pageSize;
        if (page < 1 || pageSize is < 1 or > 100) throw InvalidArgument("page/page_size is invalid.");
        return (page, pageSize);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };

    private static RpcException InvalidArgument(string detail) =>
        new(new Grpc.Core.Status(Grpc.Core.StatusCode.InvalidArgument, detail));

    private static RpcException AlreadyExists(string code) =>
        new(new Grpc.Core.Status(Grpc.Core.StatusCode.AlreadyExists, code));

    private sealed record ScheduleInput(int Day, TimeOnly Start, TimeOnly End, string Room);
    private sealed record CourseInput(Guid Id, Guid SubjectId, Guid SemesterId, string Code,
        string LecturerName, int Capacity, ScheduleInput[] Schedules);
}
