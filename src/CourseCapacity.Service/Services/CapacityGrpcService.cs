using CapacityContract = RealtimeCourseRegistration.Contracts.Capacity.V1;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using CourseCapacity.Service.Data;
using CourseCapacity.Service.Data.Entities;

namespace CourseCapacity.Service.Services;

public sealed class CapacityGrpcService(
    CapacityDbContext dbContext,
    CapacityEnrollmentService enrollmentService,
    CapacityAdminService adminService)
    : CapacityContract.CourseCapacity.CourseCapacityBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public override async Task<CapacityContract.CoursePage> ListCourses(
        CapacityContract.ListCoursesRequest request,
        ServerCallContext context)
    {
        var (page, pageSize) = ValidatePage(request.Page, request.PageSize);
        var query = dbContext.CourseSections.AsNoTracking()
            .Include(section => section.Subject)
            .Include(section => section.Semester)
            .Include(section => section.Schedules)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(section =>
                EF.Functions.ILike(section.CourseCode, $"%{search}%")
                || EF.Functions.ILike(section.Subject.Code, $"%{search}%")
                || EF.Functions.ILike(section.Subject.Name, $"%{search}%"));
        }

        if (!string.IsNullOrWhiteSpace(request.SemesterId))
        {
            var semesterId = ParseUuid(request.SemesterId, "semester_id");
            query = query.Where(section => section.SemesterId == semesterId);
        }

        if (request.Status != CapacityContract.CourseStatus.Unspecified)
        {
            query = request.Status switch
            {
                CapacityContract.CourseStatus.Open => query.Where(section => section.Status == CourseStatus.Open),
                CapacityContract.CourseStatus.Closed => query.Where(section => section.Status == CourseStatus.Closed),
                _ => throw InvalidArgument("status is invalid.")
            };
        }

        var totalCount = await query.LongCountAsync(context.CancellationToken);
        var sections = await query.OrderBy(section => section.CourseCode).ThenBy(section => section.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(context.CancellationToken);
        var response = new CapacityContract.CoursePage { Page = page, PageSize = pageSize, TotalCount = totalCount };
        response.Courses.AddRange(sections.Select(CreateSnapshot));
        return response;
    }

    public override async Task<CapacityContract.CourseSnapshot> GetCourse(
        CapacityContract.GetCourseRequest request,
        ServerCallContext context)
    {
        var courseId = ParseUuid(request.CourseSectionId, "course_section_id");
        var section = await CourseQuery().SingleOrDefaultAsync(candidate => candidate.Id == courseId, context.CancellationToken);
        return section is null
            ? throw new RpcException(new Status(StatusCode.NotFound, "COURSE_NOT_FOUND"))
            : CreateSnapshot(section);
    }

    public override Task<CapacityContract.MutationResult> ReserveSeat(
        CapacityContract.ReserveSeatRequest request,
        ServerCallContext context)
    {
        var actorId = ParseUuid(request.ActorId, "actor_id");
        var studentId = ParseUuid(request.StudentId, "student_id");
        var courseSectionId = ParseUuid(request.CourseSectionId, "course_section_id");
        var operationId = ParseUuid(request.OperationId, "operation_id");
        return enrollmentService.ReserveSeatAsync(
            actorId, studentId, courseSectionId, operationId, context.CancellationToken);
    }

    public override Task<CapacityContract.MutationResult> ReleaseSeat(
        CapacityContract.ReleaseSeatRequest request,
        ServerCallContext context)
    {
        var actorId = ParseUuid(request.ActorId, "actor_id");
        var studentId = ParseUuid(request.StudentId, "student_id");
        var registrationId = ParseUuid(request.RegistrationId, "registration_id");
        var operationId = ParseUuid(request.OperationId, "operation_id");
        return enrollmentService.ReleaseSeatAsync(
            actorId, studentId, registrationId, operationId, context.CancellationToken);
    }

    public override async Task<CapacityContract.RegistrationPage> GetStudentRegistrations(
        CapacityContract.StudentRegistrationsRequest request,
        ServerCallContext context)
    {
        var studentId = ParseUuid(request.StudentId, "student_id");
        var (page, pageSize) = ValidatePage(request.Page, request.PageSize);
        var query = RegistrationQuery().Where(item => item.StudentId == studentId);
        query = request.Status switch
        {
            CapacityContract.RegistrationStatusFilter.Unspecified
                or CapacityContract.RegistrationStatusFilter.Active =>
                query.Where(item => item.Status == RegistrationStatus.Active),
            CapacityContract.RegistrationStatusFilter.Cancelled =>
                query.Where(item => item.Status == RegistrationStatus.Cancelled),
            CapacityContract.RegistrationStatusFilter.All => query,
            _ => throw InvalidArgument("status is invalid.")
        };

        var totalCount = await query.LongCountAsync(context.CancellationToken);
        var registrations = await query.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(context.CancellationToken);
        var response = new CapacityContract.RegistrationPage
        {
            Page = page, PageSize = pageSize, TotalCount = totalCount
        };
        response.Registrations.AddRange(registrations.Select(ToRegistrationRecord));
        return response;
    }

    public override async Task<CapacityContract.StudentSummary> GetStudentSummary(
        CapacityContract.StudentSummaryRequest request,
        ServerCallContext context)
    {
        var studentId = ParseUuid(request.StudentId, "student_id");
        Guid? semesterId = null;
        if (!string.IsNullOrWhiteSpace(request.SemesterId))
        {
            semesterId = ParseUuid(request.SemesterId, "semester_id");
        }

        var query = RegistrationQuery().Where(item =>
            item.StudentId == studentId && item.Status == RegistrationStatus.Active);
        if (semesterId.HasValue)
        {
            query = query.Where(item => item.CourseSection.SemesterId == semesterId.Value);
        }

        var registrations = await query.OrderBy(item => item.CourseSection.CourseCode)
            .ThenBy(item => item.Id).ToListAsync(context.CancellationToken);
        var response = new CapacityContract.StudentSummary
        {
            TotalActiveRegistrations = registrations.Count,
            TotalCredits = registrations.Sum(item => item.CourseSection.Subject.Credits)
        };
        response.Registrations.AddRange(registrations.Select(ToRegistrationRecord));
        return response;
    }

    public override async Task<CapacityContract.PendingEventsResponse> GetPendingEvents(
        CapacityContract.PendingEventsRequest request,
        ServerCallContext context)
    {
        var batchSize = request.BatchSize == 0 ? 50 : request.BatchSize;
        if (batchSize is < 1 or > 100)
        {
            throw InvalidArgument("batch_size must be between 1 and 100.");
        }

        var events = await dbContext.OutboxMessages.AsNoTracking()
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.EventId)
            .Take(batchSize)
            .ToListAsync(context.CancellationToken);
        var response = new CapacityContract.PendingEventsResponse();
        response.Events.AddRange(events.Select(message => new CapacityContract.PendingEvent
        {
            EventId = message.EventId.ToString(),
            EventType = message.EventType,
            AggregateId = message.AggregateId.ToString(),
            Version = message.Version,
            PayloadJson = message.PayloadJson,
            OccurredAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(message.OccurredAt)
        }));
        return response;
    }

    public override async Task<CapacityContract.MarkEventPublishedResponse> MarkEventPublished(
        CapacityContract.MarkEventPublishedRequest request,
        ServerCallContext context)
    {
        var eventId = ParseUuid(request.EventId, "event_id");
        var publishedAt = DateTime.UtcNow;
        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE outbox_messages
            SET published_at = {publishedAt}
            WHERE event_id = {eventId} AND published_at IS NULL
            """, context.CancellationToken);
        return new CapacityContract.MarkEventPublishedResponse { Marked = updated == 1 };
    }

    public override async Task<CapacityContract.CapacityCheckResponse> CheckCapacity(
        CapacityContract.GetCourseRequest request,
        ServerCallContext context)
    {
        var snapshot = await GetCourse(request, context);
        var reason = snapshot.Status switch
        {
            CapacityContract.CourseStatus.Closed => CapacityContract.CapacityCheckReason.CourseClosed,
            _ when snapshot.RemainingSeats == 0 => CapacityContract.CapacityCheckReason.CourseFull,
            _ => CapacityContract.CapacityCheckReason.Available
        };
        return new CapacityContract.CapacityCheckResponse
        {
            Snapshot = snapshot,
            CanRegister = reason == CapacityContract.CapacityCheckReason.Available,
            Reason = reason
        };
    }

    public override async Task<CapacityContract.SubjectPage> ListSubjects(
        CapacityContract.ListSubjectsRequest request,
        ServerCallContext context)
    {
        var (page, pageSize) = ValidatePage(request.Page, request.PageSize);
        var query = dbContext.Subjects.AsNoTracking().AsQueryable();
        if (!request.IncludeInactive)
        {
            query = query.Where(subject => subject.IsActive);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(subject => EF.Functions.ILike(subject.Code, $"%{search}%")
                || EF.Functions.ILike(subject.Name, $"%{search}%"));
        }

        var totalCount = await query.LongCountAsync(context.CancellationToken);
        var subjects = await query.OrderBy(subject => subject.Code).ThenBy(subject => subject.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(context.CancellationToken);
        var response = new CapacityContract.SubjectPage { Page = page, PageSize = pageSize, TotalCount = totalCount };
        response.Subjects.AddRange(subjects.Select(subject => new CapacityContract.SubjectSnapshot
        {
            SubjectId = subject.Id.ToString(), Code = subject.Code, Name = subject.Name,
            Credits = subject.Credits, IsActive = subject.IsActive, Version = subject.Version
        }));
        return response;
    }

    public override async Task<CapacityContract.SemesterPage> ListSemesters(
        CapacityContract.ListSemestersRequest request,
        ServerCallContext context)
    {
        var query = dbContext.Semesters.AsNoTracking();
        if (request.ActiveOnly)
        {
            query = query.Where(semester => semester.IsActive);
        }

        var semesters = await query.OrderByDescending(semester => semester.StartDate)
            .ThenBy(semester => semester.Code).ThenBy(semester => semester.Id)
            .ToListAsync(context.CancellationToken);
        var response = new CapacityContract.SemesterPage();
        response.Semesters.AddRange(semesters.Select(semester => new CapacityContract.SemesterSnapshot
        {
            SemesterId = semester.Id.ToString(), Code = semester.Code, Name = semester.Name,
            StartDate = semester.StartDate.ToString("yyyy-MM-dd"),
            EndDate = semester.EndDate.ToString("yyyy-MM-dd"), IsActive = semester.IsActive
        }));
        return response;
    }

    public override Task<CapacityContract.SubjectMutationResult> CreateSubject(
        CapacityContract.CreateSubjectRequest request, ServerCallContext context) =>
        adminService.CreateSubjectAsync(request, context.CancellationToken);

    public override Task<CapacityContract.SubjectMutationResult> UpdateSubject(
        CapacityContract.UpdateSubjectRequest request, ServerCallContext context) =>
        adminService.UpdateSubjectAsync(request, context.CancellationToken);

    public override Task<CapacityContract.CourseMutationResult> CreateCourse(
        CapacityContract.CreateCourseRequest request, ServerCallContext context) =>
        adminService.CreateCourseAsync(request, context.CancellationToken);

    public override Task<CapacityContract.CourseMutationResult> UpdateCourse(
        CapacityContract.UpdateCourseRequest request, ServerCallContext context) =>
        adminService.UpdateCourseAsync(request, context.CancellationToken);

    public override Task<CapacityContract.MutationResult> UpdateCapacity(
        CapacityContract.UpdateCapacityRequest request, ServerCallContext context) =>
        adminService.UpdateCapacityAsync(request, context.CancellationToken);

    public override Task<CapacityContract.MutationResult> UpdateStatus(
        CapacityContract.UpdateStatusRequest request, ServerCallContext context) =>
        adminService.UpdateStatusAsync(request, context.CancellationToken);

    public override Task<CapacityContract.RegistrationPage> GetCourseRegistrations(
        CapacityContract.CourseRegistrationsRequest request, ServerCallContext context) =>
        adminService.GetCourseRegistrationsAsync(request, context.CancellationToken);

    public override Task<CapacityContract.StatisticsResponse> GetStatistics(
        CapacityContract.StatisticsRequest request, ServerCallContext context) =>
        adminService.GetStatisticsAsync(request, context.CancellationToken);

    private IQueryable<CourseSection> CourseQuery() => dbContext.CourseSections.AsNoTracking()
        .Include(section => section.Subject).Include(section => section.Semester).Include(section => section.Schedules);

    private IQueryable<Registration> RegistrationQuery() => dbContext.Registrations.AsNoTracking()
        .Include(item => item.CourseSection)
            .ThenInclude(section => section.Subject)
        .Include(item => item.CourseSection)
            .ThenInclude(section => section.Semester)
        .Include(item => item.CourseSection)
            .ThenInclude(section => section.Schedules);

    private static CapacityContract.RegistrationRecord ToRegistrationRecord(Registration item)
    {
        var record = new CapacityContract.RegistrationRecord
        {
            RegistrationId = item.Id.ToString(),
            StudentId = item.StudentId.ToString(),
            CourseSectionId = item.CourseSectionId.ToString(),
            Status = item.Status == RegistrationStatus.Active
                ? CapacityContract.RegistrationState.Active
                : CapacityContract.RegistrationState.Cancelled,
            CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(item.CreatedAt),
            Course = CreateSnapshot(item.CourseSection)
        };
        if (item.CancelledAt is not null)
        {
            record.CancelledAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(item.CancelledAt.Value);
        }
        return record;
    }

    internal static CapacityContract.CourseSnapshot CreateSnapshot(CourseSection section)
    {
        var snapshot = new CapacityContract.CourseSnapshot
        {
            CourseSectionId = section.Id.ToString(), CourseCode = section.CourseCode,
            SubjectId = section.SubjectId.ToString(), SubjectCode = section.Subject.Code,
            SubjectName = section.Subject.Name, Credits = section.Subject.Credits,
            SemesterId = section.SemesterId.ToString(), SemesterName = section.Semester.Name,
            LecturerName = section.LecturerName, Capacity = section.Capacity,
            RegisteredCount = section.RegisteredCount, RemainingSeats = section.Capacity - section.RegisteredCount,
            Status = section.Status == CourseStatus.Open ? CapacityContract.CourseStatus.Open : CapacityContract.CourseStatus.Closed,
            Version = section.Version
        };
        snapshot.Schedules.AddRange(section.Schedules.OrderBy(schedule => schedule.DayOfWeek)
            .ThenBy(schedule => schedule.StartTime).Select(schedule => new CapacityContract.CourseSchedule
            {
                DayOfWeek = schedule.DayOfWeek, StartTime = schedule.StartTime.ToString("HH:mm:ss"),
                EndTime = schedule.EndTime.ToString("HH:mm:ss"), Room = schedule.Room
            }));
        return snapshot;
    }

    private static (int Page, int PageSize) ValidatePage(int requestedPage, int requestedPageSize)
    {
        var page = requestedPage == 0 ? 1 : requestedPage;
        var pageSize = requestedPageSize == 0 ? DefaultPageSize : requestedPageSize;
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            throw InvalidArgument($"page must be at least 1 and page_size must be between 1 and {MaxPageSize}.");
        }
        return (page, pageSize);
    }

    private static Guid ParseUuid(string value, string fieldName)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw InvalidArgument($"{fieldName} must be a non-empty UUID.");
        }
        return id;
    }

    private static RpcException InvalidArgument(string detail) => new(new Status(StatusCode.InvalidArgument, detail));
}
