using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Registration.Api.Data;
using Registration.Api.Dtos;
using Registration.Api.Middleware;
using RealtimeCourseRegistration.Contracts.Capacity.V1;
using RealtimeCourseRegistration.Contracts.Common;
using Capacity = RealtimeCourseRegistration.Contracts.Capacity.V1;

namespace Registration.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin").WithTags("Administration").RequireAuthorization("Admin");
        group.MapGet("/students", ListStudentsAsync);
        group.MapGet("/subjects", ListSubjectsAsync);
        group.MapPost("/subjects", CreateSubjectAsync).RequireRateLimiting("mutations");
        group.MapPut("/subjects/{id:guid}", UpdateSubjectAsync).RequireRateLimiting("mutations");
        group.MapGet("/courses", ListCoursesAsync);
        group.MapPost("/courses", CreateCourseAsync).RequireRateLimiting("mutations");
        group.MapPut("/courses/{id:guid}", UpdateCourseAsync).RequireRateLimiting("mutations");
        group.MapPatch("/courses/{id:guid}/capacity", UpdateCapacityAsync).RequireRateLimiting("mutations");
        group.MapPatch("/courses/{id:guid}/status", UpdateStatusAsync).RequireRateLimiting("mutations");
        group.MapGet("/courses/{id:guid}/registrations", ListCourseRegistrationsAsync);
        group.MapGet("/statistics", StatisticsAsync);
        group.MapGet("/audit-logs", AuditLogsAsync);
        return endpoints;
    }

    private static async Task<IResult> ListStudentsAsync(
        string? search, int page, int pageSize, HttpContext context,
        IdentityDbContext identity, CancellationToken cancellationToken)
    {
        if (!ValidPage(page, pageSize) || search?.Length > 128)
            return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "page/pageSize/search không hợp lệ.");
        var query = identity.Students.AsNoTracking().Include(student => student.User).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(student => EF.Functions.ILike(student.StudentCode, $"%{term}%")
                || EF.Functions.ILike(student.FullName, $"%{term}%")
                || (student.Email != null && EF.Functions.ILike(student.Email, $"%{term}%"))
                || EF.Functions.ILike(student.User.Username, $"%{term}%"));
        }
        var count = await query.LongCountAsync(cancellationToken);
        var students = await query.OrderBy(student => student.StudentCode).ThenBy(student => student.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(student => new AdminStudentDto(
                student.Id, student.StudentCode, student.FullName, student.Email, student.Major,
                student.User.Username, student.User.IsActive)).ToArrayAsync(cancellationToken);
        return Results.Ok(new PagedResult<AdminStudentDto>(students, page, pageSize, count));
    }

    private static async Task<IResult> ListSubjectsAsync(
        int page, int pageSize, string? search, HttpContext context,
        Capacity.CourseCapacity.CourseCapacityClient capacity, CancellationToken cancellationToken)
    {
        if (!ValidPage(page, pageSize) || search?.Length > 128)
            return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "page/pageSize/search không hợp lệ.");
        try
        {
            var result = await capacity.ListSubjectsAsync(new ListSubjectsRequest
            {
                Page = page, PageSize = pageSize, Search = search?.Trim() ?? string.Empty,
                IncludeInactive = true
            }, CourseEndpoints.CreateHeaders(context), Deadline(), cancellationToken);
            return Results.Ok(new PagedResult<SubjectDto>(result.Subjects.Select(item => new SubjectDto(
                Guid.Parse(item.SubjectId), item.Code, item.Name, item.Credits, item.IsActive, item.Version)).ToArray(),
                result.Page, result.PageSize, result.TotalCount));
        }
        catch (RpcException exception) { return ApiProblemResults.FromGrpc(context, exception); }
    }

    private static async Task<IResult> CreateSubjectAsync(
        SubjectInput input, HttpContext context, Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (!TryMutationIdentity(context, out var actorId, out var operationId, out var error)) return error!;
        try
        {
            var result = await capacity.CreateSubjectAsync(new CreateSubjectRequest
            {
                ActorId = actorId.ToString(), Code = input.Code ?? string.Empty,
                Name = input.Name ?? string.Empty, Credits = input.Credits, OperationId = operationId.ToString()
            }, CourseEndpoints.CreateHeaders(context), Deadline(), cancellationToken);
            return SubjectMutationResponse(context, result, StatusCodes.Status201Created);
        }
        catch (RpcException exception) { return ApiProblemResults.FromGrpc(context, exception); }
    }

    private static async Task<IResult> UpdateSubjectAsync(
        string id, SubjectUpdateInput input, HttpContext context,
        Capacity.CourseCapacity.CourseCapacityClient capacity, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var subjectId) || subjectId == Guid.Empty)
            return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "subjectId không hợp lệ.");
        if (!TryMutationIdentity(context, out var actorId, out var operationId, out var error)) return error!;
        try
        {
            var result = await capacity.UpdateSubjectAsync(new UpdateSubjectRequest
            {
                ActorId = actorId.ToString(), SubjectId = subjectId.ToString(), OperationId = operationId.ToString(),
                Code = input.Code ?? string.Empty, Name = input.Name ?? string.Empty, Credits = input.Credits,
                IsActive = input.IsActive, ExpectedVersion = input.ExpectedVersion
            }, CourseEndpoints.CreateHeaders(context), Deadline(), cancellationToken);
            return SubjectMutationResponse(context, result, StatusCodes.Status200OK);
        }
        catch (RpcException exception) { return ApiProblemResults.FromGrpc(context, exception); }
    }

    private static async Task<IResult> ListCoursesAsync(
        string? search, string? semesterId, string? status, int page, int pageSize, HttpContext context,
        Capacity.CourseCapacity.CourseCapacityClient capacity, CancellationToken cancellationToken)
    {
        if (!ValidPage(page, pageSize) || search?.Length > 128)
            return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "page/pageSize/search không hợp lệ.");
        var request = new ListCoursesRequest { Search = search?.Trim() ?? string.Empty, Page = page, PageSize = pageSize };
        if (!string.IsNullOrWhiteSpace(semesterId))
        {
            if (!Guid.TryParse(semesterId, out var semester) || semester == Guid.Empty)
                return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "semesterId không hợp lệ.");
            request.SemesterId = semester.ToString();
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<Capacity.CourseStatus>(status, true, out var parsed) || parsed == Capacity.CourseStatus.Unspecified)
                return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "status chỉ nhận Open hoặc Closed.");
            request.Status = parsed;
        }
        try
        {
            var result = await capacity.ListCoursesAsync(request, CourseEndpoints.CreateHeaders(context), Deadline(), cancellationToken);
            return Results.Ok(new PagedResult<CourseDto>(result.Courses.Select(CourseEndpoints.ToDto).ToArray(),
                result.Page, result.PageSize, result.TotalCount));
        }
        catch (RpcException exception) { return ApiProblemResults.FromGrpc(context, exception); }
    }

    private static async Task<IResult> CreateCourseAsync(
        CourseInput input, HttpContext context, Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (!TryMutationIdentity(context, out var actorId, out var operationId, out var error)) return error!;
        if (!TryBuildCourseInput(input, Guid.NewGuid(), out var course, out var validationError))
            return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, validationError);
        try
        {
            var result = await capacity.CreateCourseAsync(new CreateCourseRequest
            {
                ActorId = actorId.ToString(), OperationId = operationId.ToString(), Course = course
            }, CourseEndpoints.CreateHeaders(context), Deadline(), cancellationToken);
            return CourseMutationResponse(context, result, StatusCodes.Status201Created);
        }
        catch (RpcException exception) { return ApiProblemResults.FromGrpc(context, exception); }
    }

    private static async Task<IResult> UpdateCourseAsync(
        Guid id, CourseUpdateInput input, HttpContext context, Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "courseId không hợp lệ.");
        if (!TryMutationIdentity(context, out var actorId, out var operationId, out var error)) return error!;
        if (!TryBuildCourseInput(new CourseInput(input.CourseCode, input.SubjectId, input.SemesterId,
                input.LecturerName, input.Capacity, input.Schedules), id, out var course, out var validationError))
            return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, validationError);
        try
        {
            var result = await capacity.UpdateCourseAsync(new UpdateCourseRequest
            {
                ActorId = actorId.ToString(), OperationId = operationId.ToString(),
                ExpectedVersion = input.ExpectedVersion, Course = course
            }, CourseEndpoints.CreateHeaders(context), Deadline(), cancellationToken);
            return CourseMutationResponse(context, result, StatusCodes.Status200OK);
        }
        catch (RpcException exception) { return ApiProblemResults.FromGrpc(context, exception); }
    }

    private static async Task<IResult> UpdateCapacityAsync(
        Guid id, CapacityInput input, HttpContext context,
        Capacity.CourseCapacity.CourseCapacityClient capacity, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "courseId không hợp lệ.");
        if (!TryMutationIdentity(context, out var actorId, out var operationId, out var error)) return error!;
        try
        {
            var result = await capacity.UpdateCapacityAsync(new UpdateCapacityRequest
            {
                ActorId = actorId.ToString(), OperationId = operationId.ToString(),
                CourseSectionId = id.ToString(), Capacity = input.Capacity, ExpectedVersion = input.ExpectedVersion
            }, CourseEndpoints.CreateHeaders(context), Deadline(), cancellationToken);
            return MutationResponse(context, result);
        }
        catch (RpcException exception) { return ApiProblemResults.FromGrpc(context, exception); }
    }

    private static async Task<IResult> UpdateStatusAsync(
        Guid id, StatusInput input, HttpContext context,
        Capacity.CourseCapacity.CourseCapacityClient capacity, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "courseId không hợp lệ.");
        if (!TryMutationIdentity(context, out var actorId, out var operationId, out var error)) return error!;
        if (!Enum.TryParse<Capacity.CourseStatus>(input.Status, true, out var status)
            || status == Capacity.CourseStatus.Unspecified)
            return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "status chỉ nhận Open hoặc Closed.");
        try
        {
            var result = await capacity.UpdateStatusAsync(new UpdateStatusRequest
            {
                ActorId = actorId.ToString(), OperationId = operationId.ToString(),
                CourseSectionId = id.ToString(), Status = status, ExpectedVersion = input.ExpectedVersion
            }, CourseEndpoints.CreateHeaders(context), Deadline(), cancellationToken);
            return MutationResponse(context, result);
        }
        catch (RpcException exception) { return ApiProblemResults.FromGrpc(context, exception); }
    }

    private static async Task<IResult> ListCourseRegistrationsAsync(
        Guid id, int page, int pageSize, HttpContext context, IdentityDbContext identity,
        Capacity.CourseCapacity.CourseCapacityClient capacity, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || !ValidPage(page, pageSize))
            return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "courseId/page/pageSize không hợp lệ.");
        try
        {
            var result = await capacity.GetCourseRegistrationsAsync(new CourseRegistrationsRequest
            {
                CourseSectionId = id.ToString(), Page = page, PageSize = pageSize
            }, CourseEndpoints.CreateHeaders(context), Deadline(), cancellationToken);
            var ids = result.Registrations.Select(item => Guid.Parse(item.StudentId)).Distinct().ToArray();
            var profiles = await identity.Students.AsNoTracking().Where(student => ids.Contains(student.Id))
                .Select(student => new { student.Id, student.StudentCode, student.FullName, student.Email, student.Major })
                .ToDictionaryAsync(student => student.Id, cancellationToken);
            var items = result.Registrations.Select(item =>
            {
                profiles.TryGetValue(Guid.Parse(item.StudentId), out var student);
                return new CourseRegistrationDto(
                    Guid.Parse(item.RegistrationId), Guid.Parse(item.StudentId), student?.StudentCode,
                    student?.FullName, student?.Email, student?.Major, item.Status.ToString(),
                    item.CreatedAt.ToDateTimeOffset(), item.CancelledAt?.ToDateTimeOffset(),
                    CourseEndpoints.ToDto(item.Course));
            }).ToArray();
            return Results.Ok(new PagedResult<CourseRegistrationDto>(items, result.Page, result.PageSize, result.TotalCount));
        }
        catch (RpcException exception) { return ApiProblemResults.FromGrpc(context, exception); }
    }

    private static async Task<IResult> StatisticsAsync(
        string? semesterId, HttpContext context, IdentityDbContext identity,
        Capacity.CourseCapacity.CourseCapacityClient capacity, CancellationToken cancellationToken)
    {
        var request = new StatisticsRequest();
        if (!string.IsNullOrWhiteSpace(semesterId))
        {
            if (!Guid.TryParse(semesterId, out var semester) || semester == Guid.Empty)
                return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "semesterId không hợp lệ.");
            request.SemesterId = semester.ToString();
        }
        try
        {
            var stats = await capacity.GetStatisticsAsync(request, CourseEndpoints.CreateHeaders(context), Deadline(), cancellationToken);
            var studentCount = await identity.Students.LongCountAsync(cancellationToken);
            return Results.Ok(new AdminStatisticsDto(studentCount, stats.CourseCount,
                stats.ActiveRegistrationCount, stats.FullCourseCount));
        }
        catch (RpcException exception) { return ApiProblemResults.FromGrpc(context, exception); }
    }

    private static async Task<IResult> AuditLogsAsync(
        Guid? courseSectionId, Guid? studentId, string? eventType, DateTimeOffset? from, DateTimeOffset? to,
        int page, int pageSize, HttpContext context, IConfiguration configuration, IHttpClientFactory clients,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["Audit:InternalApiKey"];
        var baseAddress = configuration["Audit:BaseAddress"];
        if (string.IsNullOrWhiteSpace(apiKey) || !Uri.TryCreate(baseAddress, UriKind.Absolute, out var workerUri))
            return ApiProblemResults.Create(context, 503, ErrorCodes.DependencyUnavailable, "Audit Worker chưa được cấu hình.");
        if (!ValidPage(page, pageSize) || eventType?.Length > 64 || courseSectionId == Guid.Empty
            || studentId == Guid.Empty || (from.HasValue && to.HasValue && from > to))
            return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "Bộ lọc audit không hợp lệ.");

        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (courseSectionId.HasValue) query.Add($"courseSectionId={courseSectionId.Value:D}");
        if (studentId.HasValue) query.Add($"studentId={studentId.Value:D}");
        if (!string.IsNullOrWhiteSpace(eventType)) query.Add($"eventType={Uri.EscapeDataString(eventType)}");
        if (from.HasValue) query.Add($"from={Uri.EscapeDataString(from.Value.ToUniversalTime().ToString("O"))}");
        if (to.HasValue) query.Add($"to={Uri.EscapeDataString(to.Value.ToUniversalTime().ToString("O"))}");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(workerUri, $"/internal/audit-logs?{string.Join('&', query)}"));
        request.Headers.Add("X-Audit-Internal-Key", apiKey);
        request.Headers.Add("X-Correlation-ID", CorrelationIdMiddleware.Get(context).ToString());
        try
        {
            using var response = await clients.CreateClient("audit").SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest)
                return ApiProblemResults.Create(context, 400, ErrorCodes.ValidationError, "Bộ lọc audit không hợp lệ.");
            if (!response.IsSuccessStatusCode)
                return ApiProblemResults.Create(context, 503, ErrorCodes.DependencyUnavailable, "Không thể truy vấn audit log lúc này.");
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return Results.Content(json, "application/json; charset=utf-8", statusCode: StatusCodes.Status200OK);
        }
        catch (HttpRequestException)
        {
            return ApiProblemResults.Create(context, 503, ErrorCodes.DependencyUnavailable, "Audit Worker tạm thời không khả dụng.");
        }
    }

    private static bool TryBuildCourseInput(CourseInput input, Guid id,
        out Capacity.CourseMutationInput course, out string error)
    {
        course = new Capacity.CourseMutationInput { CourseSectionId = id.ToString() };
        error = "Thông tin lớp không hợp lệ.";
        if (input.CourseCode?.Length > 48 || input.LecturerName?.Length > 160 || input.Schedules is null || input.Schedules.Count > 20)
            return false;
        course.CourseCode = input.CourseCode?.Trim() ?? string.Empty;
        course.SubjectId = input.SubjectId.ToString();
        course.SemesterId = input.SemesterId.ToString();
        course.LecturerName = input.LecturerName?.Trim() ?? string.Empty;
        course.Capacity = input.Capacity;
        foreach (var item in input.Schedules)
        {
            course.Schedules.Add(new Capacity.CourseSchedule
            {
                DayOfWeek = item.DayOfWeek, StartTime = item.StartTime ?? string.Empty,
                EndTime = item.EndTime ?? string.Empty, Room = item.Room ?? string.Empty
            });
        }
        return input.SubjectId != Guid.Empty && input.SemesterId != Guid.Empty
            && !string.IsNullOrWhiteSpace(course.CourseCode) && !string.IsNullOrWhiteSpace(course.LecturerName)
            && input.Capacity > 0;
    }

    private static IResult SubjectMutationResponse(HttpContext context,
        Capacity.SubjectMutationResult result, int successStatus)
    {
        if (result.Outcome == Capacity.OperationOutcome.Succeeded)
            return Results.Json(new SubjectDto(Guid.Parse(result.Subject.SubjectId), result.Subject.Code,
                result.Subject.Name, result.Subject.Credits, result.Subject.IsActive, result.Subject.Version), statusCode: successStatus);
        return OutcomeProblem(context, result.Outcome, result.ErrorCode);
    }

    private static IResult CourseMutationResponse(HttpContext context,
        Capacity.CourseMutationResult result, int successStatus)
    {
        if (result.Outcome == Capacity.OperationOutcome.Succeeded)
            return Results.Json(CourseEndpoints.ToDto(result.Course), statusCode: successStatus);
        return OutcomeProblem(context, result.Outcome, result.ErrorCode);
    }

    private static IResult MutationResponse(HttpContext context, Capacity.MutationResult result)
    {
        if (result.Outcome == Capacity.OperationOutcome.Succeeded)
            return Results.Ok(CourseEndpoints.ToDto(result.Snapshot));
        return OutcomeProblem(context, result.Outcome, result.ErrorCode);
    }

    private static IResult OutcomeProblem(HttpContext context, Capacity.OperationOutcome outcome, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) code = OutcomeErrorCodeMap.ToErrorCode(outcome) ?? ErrorCodes.InternalError;
        var status = outcome switch
        {
            Capacity.OperationOutcome.CourseNotFound => 404,
            Capacity.OperationOutcome.VersionConflict or Capacity.OperationOutcome.InvalidCapacity
                or Capacity.OperationOutcome.IdempotencyConflict => 409,
            Capacity.OperationOutcome.ValidationError when code is "COURSE_CODE_EXISTS" or "SUBJECT_CODE_EXISTS" => 409,
            Capacity.OperationOutcome.ValidationError => 400,
            _ => 500
        };
        var detail = code switch
        {
            "VERSION_CONFLICT" => "Dữ liệu đã được thay đổi bởi thao tác khác. Tải lại trước khi tiếp tục.",
            "INVALID_CAPACITY" => "Sức chứa phải lớn hơn 0 và không thấp hơn số lượt đăng ký hiện tại.",
            "COURSE_CODE_EXISTS" or "SUBJECT_CODE_EXISTS" => "Mã đã được sử dụng.",
            "COURSE_HAS_REGISTRATIONS" => "Không thể chuyển môn hoặc học kỳ khi lớp đã có đăng ký.",
            "IDEMPOTENCY_CONFLICT" => "Idempotency-Key đã được dùng cho thao tác khác.",
            _ => "Thao tác quản trị không thể hoàn tất."
        };
        return ApiProblemResults.Create(context, status, code, detail);
    }

    private static bool TryMutationIdentity(HttpContext context, out Guid actorId,
        out Guid operationId, out IResult? error)
    {
        error = null;
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out actorId) || actorId == Guid.Empty)
        {
            operationId = Guid.Empty;
            error = ApiProblemResults.Create(context, 401, ErrorCodes.Unauthenticated);
            return false;
        }
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out operationId) || operationId == Guid.Empty)
        {
            error = ApiProblemResults.Create(context, 400, ErrorCodes.InvalidIdempotencyKey,
                "Gửi Idempotency-Key hợp lệ dưới dạng UUID.");
            return false;
        }
        return true;
    }

    private static bool ValidPage(int page, int pageSize) => page >= 1 && pageSize is >= 1 and <= 100;
    private static DateTime Deadline() => DateTime.UtcNow.AddSeconds(5);

    public sealed record AdminStudentDto(Guid StudentId, string StudentCode, string FullName,
        string? Email, string? Major, string Username, bool IsActive);
    public sealed record AdminStatisticsDto(long TotalStudents, long TotalCourses,
        long ActiveRegistrations, long FullCourses);
    public sealed record CourseRegistrationDto(Guid RegistrationId, Guid StudentId, string? StudentCode,
        string? FullName, string? Email, string? Major, string Status, DateTimeOffset CreatedAt,
        DateTimeOffset? CancelledAt, CourseDto Course);
    public sealed record SubjectInput(string? Code, string? Name, int Credits);
    public sealed record SubjectUpdateInput(string? Code, string? Name, int Credits,
        bool IsActive, long ExpectedVersion);
    public sealed record CourseInput(string? CourseCode, Guid SubjectId, Guid SemesterId,
        string? LecturerName, int Capacity, IReadOnlyList<ScheduleInput>? Schedules);
    public sealed record CourseUpdateInput(string? CourseCode, Guid SubjectId, Guid SemesterId,
        string? LecturerName, int Capacity, long ExpectedVersion, IReadOnlyList<ScheduleInput>? Schedules);
    public sealed record ScheduleInput(int DayOfWeek, string? StartTime, string? EndTime, string? Room);
    public sealed record CapacityInput(int Capacity, long ExpectedVersion);
    public sealed record StatusInput(string? Status, long ExpectedVersion);
}
