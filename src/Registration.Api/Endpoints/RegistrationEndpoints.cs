using System.Security.Claims;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Registration.Api.Data;
using Registration.Api.Dtos;
using Registration.Api.Middleware;
using RealtimeCourseRegistration.Contracts.Capacity.V1;
using RealtimeCourseRegistration.Contracts.Common;
using Capacity = RealtimeCourseRegistration.Contracts.Capacity.V1;

namespace Registration.Api.Endpoints;

public static class RegistrationEndpoints
{
    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/registrations").WithTags("Registrations");
        group.MapPost("/", CreateAsync).RequireAuthorization("Student").RequireRateLimiting("mutations");
        group.MapDelete("/{id}", CancelAsync).RequireAuthorization("Student").RequireRateLimiting("mutations");

        endpoints.MapGet("/students/{studentId}/registrations", ListStudentAsync)
            .WithTags("Registrations").RequireAuthorization();
        endpoints.MapGet("/students/me/summary", GetMySummaryAsync)
            .WithTags("Students").RequireAuthorization("Student");
        endpoints.MapGet("/students/me/schedule", GetMyScheduleAsync)
            .WithTags("Students").RequireAuthorization("Student");
        endpoints.MapGet("/subjects", ListSubjectsAsync).WithTags("Subjects").RequireAuthorization();
        endpoints.MapGet("/semesters", ListSemestersAsync).WithTags("Semesters").RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        RegistrationRequest request,
        HttpContext httpContext,
        Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(httpContext.User, out var actorId, out var studentId))
        {
            return ApiProblemResults.Create(httpContext, 401, ErrorCodes.Unauthenticated);
        }
        if (!TryGetOperationId(httpContext, out var operationId))
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.InvalidIdempotencyKey,
                "Gửi Idempotency-Key hợp lệ dưới dạng UUID.");
        }
        if (request.CourseSectionId == Guid.Empty)
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError, "CourseSectionId không hợp lệ.");
        }

        try
        {
            var result = await capacity.ReserveSeatAsync(new Capacity.ReserveSeatRequest
            {
                ActorId = actorId.ToString(),
                StudentId = studentId.ToString(),
                CourseSectionId = request.CourseSectionId.ToString(),
                OperationId = operationId.ToString()
            }, CourseEndpoints.CreateHeaders(httpContext), DateTime.UtcNow.AddSeconds(5), cancellationToken);
            if (result.Outcome == Capacity.OperationOutcome.Succeeded)
            {
                return Results.Created((string?)null, ToResult(result));
            }
            return OutcomeProblem(httpContext, result.Outcome, result.ErrorCode);
        }
        catch (RpcException exception)
        {
            return ApiProblemResults.FromGrpc(httpContext, exception);
        }
    }

    private static async Task<IResult> CancelAsync(
        string id,
        HttpContext httpContext,
        Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var registrationId) || registrationId == Guid.Empty)
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError, "Mã đăng ký không hợp lệ.");
        }
        if (!TryGetIdentity(httpContext.User, out var actorId, out var studentId))
        {
            return ApiProblemResults.Create(httpContext, 401, ErrorCodes.Unauthenticated);
        }
        if (!TryGetOperationId(httpContext, out var operationId))
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.InvalidIdempotencyKey,
                "Gửi Idempotency-Key hợp lệ dưới dạng UUID.");
        }

        try
        {
            var result = await capacity.ReleaseSeatAsync(new Capacity.ReleaseSeatRequest
            {
                ActorId = actorId.ToString(),
                StudentId = studentId.ToString(),
                RegistrationId = registrationId.ToString(),
                OperationId = operationId.ToString()
            }, CourseEndpoints.CreateHeaders(httpContext), DateTime.UtcNow.AddSeconds(5), cancellationToken);
            return result.Outcome == Capacity.OperationOutcome.Succeeded
                ? Results.NoContent()
                : OutcomeProblem(httpContext, result.Outcome, result.ErrorCode);
        }
        catch (RpcException exception)
        {
            return ApiProblemResults.FromGrpc(httpContext, exception);
        }
    }

    private static async Task<IResult> ListStudentAsync(
        string studentId,
        string? status,
        int page,
        int pageSize,
        HttpContext httpContext,
        IdentityDbContext identity,
        Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(studentId, out var requestedStudentId) || requestedStudentId == Guid.Empty)
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError, "studentId không hợp lệ.");
        }
        var role = httpContext.User.FindFirstValue(ClaimTypes.Role);
        if (role == "Student"
            && (!Guid.TryParse(httpContext.User.FindFirstValue("student_id"), out var ownerId)
                || ownerId != requestedStudentId))
        {
            return ApiProblemResults.Create(httpContext, 403, ErrorCodes.Forbidden);
        }
        if (role is not ("Student" or "Admin"))
        {
            return ApiProblemResults.Create(httpContext, 403, ErrorCodes.Forbidden);
        }
        if (!await identity.Students.AnyAsync(student => student.Id == requestedStudentId, cancellationToken))
        {
            return ApiProblemResults.Create(httpContext, 404, ErrorCodes.StudentNotFound);
        }
        if (!TryParseRegistrationStatus(status, out var filter))
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError,
                "status chỉ nhận Active, Cancelled hoặc All.");
        }
        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError, "page/pageSize không hợp lệ.");
        }

        try
        {
            var result = await capacity.GetStudentRegistrationsAsync(new StudentRegistrationsRequest
            {
                StudentId = requestedStudentId.ToString(),
                Status = filter,
                Page = page,
                PageSize = pageSize
            }, CourseEndpoints.CreateHeaders(httpContext), DateTime.UtcNow.AddSeconds(3), cancellationToken);
            var items = result.Registrations.Select(ToRegistrationDto).ToArray();
            return Results.Ok(new PagedResult<RegistrationDto>(items, result.Page, result.PageSize, result.TotalCount));
        }
        catch (RpcException exception)
        {
            return ApiProblemResults.FromGrpc(httpContext, exception);
        }
    }

    private static async Task<IResult> GetMySummaryAsync(
        HttpContext httpContext,
        Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(httpContext.User, out _, out var studentId))
        {
            return ApiProblemResults.Create(httpContext, 401, ErrorCodes.Unauthenticated);
        }
        try
        {
            var summary = await capacity.GetStudentSummaryAsync(
                new StudentSummaryRequest { StudentId = studentId.ToString() },
                CourseEndpoints.CreateHeaders(httpContext), DateTime.UtcNow.AddSeconds(3), cancellationToken);
            return Results.Ok(new StudentSummaryDto(summary.TotalActiveRegistrations, summary.TotalCredits));
        }
        catch (RpcException exception)
        {
            return ApiProblemResults.FromGrpc(httpContext, exception);
        }
    }

    private static async Task<IResult> GetMyScheduleAsync(
        Guid? semesterId,
        HttpContext httpContext,
        Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(httpContext.User, out _, out var studentId))
        {
            return ApiProblemResults.Create(httpContext, 401, ErrorCodes.Unauthenticated);
        }
        if (semesterId == Guid.Empty)
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError, "semesterId không hợp lệ.");
        }
        try
        {
            var request = new StudentSummaryRequest { StudentId = studentId.ToString() };
            if (semesterId.HasValue)
            {
                request.SemesterId = semesterId.Value.ToString();
            }
            var summary = await capacity.GetStudentSummaryAsync(request,
                CourseEndpoints.CreateHeaders(httpContext), DateTime.UtcNow.AddSeconds(3), cancellationToken);
            var schedule = summary.Registrations.SelectMany(registration =>
                registration.Course.Schedules.Select(item => new StudentScheduleDto(
                    registration.CourseSectionId,
                    registration.Course.CourseCode,
                    item.DayOfWeek,
                    item.StartTime,
                    item.EndTime,
                    item.Room))).ToArray();
            return Results.Ok(schedule);
        }
        catch (RpcException exception)
        {
            return ApiProblemResults.FromGrpc(httpContext, exception);
        }
    }

    private static async Task<IResult> ListSubjectsAsync(
        int page,
        int pageSize,
        string? search,
        HttpContext httpContext,
        Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError, "page/pageSize không hợp lệ.");
        }
        try
        {
            var result = await capacity.ListSubjectsAsync(new Capacity.ListSubjectsRequest
            {
                Page = page, PageSize = pageSize, Search = search?.Trim() ?? string.Empty
            }, CourseEndpoints.CreateHeaders(httpContext), DateTime.UtcNow.AddSeconds(3), cancellationToken);
            return Results.Ok(new PagedResult<SubjectDto>(
                result.Subjects.Select(subject => new SubjectDto(
                    Guid.Parse(subject.SubjectId), subject.Code, subject.Name, subject.Credits, subject.IsActive, subject.Version)).ToArray(),
                result.Page, result.PageSize, result.TotalCount));
        }
        catch (RpcException exception)
        {
            return ApiProblemResults.FromGrpc(httpContext, exception);
        }
    }

    private static async Task<IResult> ListSemestersAsync(
        bool activeOnly,
        HttpContext httpContext,
        Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await capacity.ListSemestersAsync(new Capacity.ListSemestersRequest { ActiveOnly = activeOnly },
                CourseEndpoints.CreateHeaders(httpContext), DateTime.UtcNow.AddSeconds(3), cancellationToken);
            return Results.Ok(result.Semesters.Select(semester => new SemesterDto(
                Guid.Parse(semester.SemesterId), semester.Code, semester.Name,
                semester.StartDate, semester.EndDate, semester.IsActive)).ToArray());
        }
        catch (RpcException exception)
        {
            return ApiProblemResults.FromGrpc(httpContext, exception);
        }
    }

    private static RegistrationResultDto ToResult(Capacity.MutationResult result) => new(
        Guid.Parse(result.RegistrationId),
        Guid.Parse(result.StudentId),
        Guid.Parse(result.CourseSectionId),
        result.RegistrationStatus.ToString(),
        result.Snapshot.Capacity,
        result.Snapshot.RegisteredCount,
        result.Snapshot.RemainingSeats,
        result.Snapshot.Version,
        result.CreatedAt.ToDateTimeOffset());

    private static RegistrationDto ToRegistrationDto(Capacity.RegistrationRecord item) => new(
        Guid.Parse(item.RegistrationId),
        Guid.Parse(item.StudentId),
        Guid.Parse(item.CourseSectionId),
        item.Status.ToString(),
        item.CreatedAt.ToDateTimeOffset(),
        item.CancelledAt is null ? null : item.CancelledAt.ToDateTimeOffset(),
        CourseEndpoints.ToDto(item.Course));

    private static bool TryGetIdentity(ClaimsPrincipal principal, out Guid actorId, out Guid studentId)
    {
        var actorValid = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out actorId)
            && actorId != Guid.Empty;
        var studentValid = Guid.TryParse(principal.FindFirstValue("student_id"), out studentId)
            && studentId != Guid.Empty;
        return actorValid && studentValid;
    }

    private static bool TryGetOperationId(HttpContext context, out Guid operationId)
    {
        return Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out operationId)
            && operationId != Guid.Empty;
    }

    private static bool TryParseRegistrationStatus(string? status, out Capacity.RegistrationStatusFilter filter)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            filter = Capacity.RegistrationStatusFilter.Active;
            return true;
        }
        if (status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            filter = Capacity.RegistrationStatusFilter.Cancelled;
            return true;
        }
        if (status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            filter = Capacity.RegistrationStatusFilter.All;
            return true;
        }
        filter = Capacity.RegistrationStatusFilter.Unspecified;
        return false;
    }

    private static IResult OutcomeProblem(HttpContext context, Capacity.OperationOutcome outcome, string errorCode)
    {
        var code = string.IsNullOrWhiteSpace(errorCode)
            ? OutcomeErrorCodeMap.ToErrorCode(outcome) ?? ErrorCodes.InternalError
            : errorCode;
        var status = outcome switch
        {
            Capacity.OperationOutcome.CourseNotFound or Capacity.OperationOutcome.RegistrationNotFound => 404,
            Capacity.OperationOutcome.Forbidden => 403,
            Capacity.OperationOutcome.AlreadyRegistered or Capacity.OperationOutcome.CourseFull
                or Capacity.OperationOutcome.CourseClosed or Capacity.OperationOutcome.IdempotencyConflict
                or Capacity.OperationOutcome.VersionConflict or Capacity.OperationOutcome.InvalidCapacity => 409,
            Capacity.OperationOutcome.ValidationError => 400,
            _ => 500
        };
        var detail = code switch
        {
            ErrorCodes.AlreadyRegistered => "Bạn đã đăng ký lớp học phần này.",
            ErrorCodes.CourseFull => "Lớp học phần đã đủ chỗ.",
            ErrorCodes.CourseClosed => "Lớp học phần đã đóng.",
            ErrorCodes.IdempotencyConflict => "Idempotency-Key đã được dùng cho nội dung khác.",
            ErrorCodes.RegistrationNotFound => "Không tìm thấy đăng ký thuộc về bạn.",
            _ => "Thao tác không thể hoàn tất."
        };
        return ApiProblemResults.Create(context, status, code, detail);
    }
}

public sealed record StudentScheduleDto(
    string CourseSectionId, string CourseCode, int DayOfWeek, string StartTime, string EndTime, string Room);
public sealed record SubjectDto(Guid Id, string Code, string Name, int Credits, bool IsActive, long Version);
public sealed record SemesterDto(Guid Id, string Code, string Name, string StartDate, string EndDate, bool IsActive);
