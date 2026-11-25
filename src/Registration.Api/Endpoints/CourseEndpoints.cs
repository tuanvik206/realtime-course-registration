using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Registration.Api.Dtos;
using Registration.Api.Middleware;
using Capacity = RealtimeCourseRegistration.Contracts.Capacity.V1;
using RealtimeCourseRegistration.Contracts.Common;

namespace Registration.Api.Endpoints;

public static class CourseEndpoints
{
    public static IEndpointRouteBuilder MapCourseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/courses").WithTags("Courses").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapGet("/{id}", GetAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] CourseListQuery query,
        HttpContext httpContext,
        Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > 100 || query.Search?.Length > 128)
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError,
                "page phải từ 1, pageSize từ 1 đến 100 và search không quá 128 ký tự.");
        }

        var request = new Capacity.ListCoursesRequest
        {
            Search = query.Search?.Trim() ?? string.Empty,
            Page = query.Page,
            PageSize = query.PageSize
        };
        if (!string.IsNullOrWhiteSpace(query.SemesterId))
        {
            if (!Guid.TryParse(query.SemesterId, out var semesterId) || semesterId == Guid.Empty)
            {
                return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError, "semesterId phải là UUID hợp lệ.");
            }
            request.SemesterId = semesterId.ToString();
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<Capacity.CourseStatus>(query.Status, true, out var status)
                || status == Capacity.CourseStatus.Unspecified)
            {
                return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError, "status chỉ nhận Open hoặc Closed.");
            }
            request.Status = status;
        }

        try
        {
            var result = await capacity.ListCoursesAsync(request, CreateHeaders(httpContext),
                DateTime.UtcNow.AddSeconds(3), cancellationToken);
            return Results.Ok(new PagedResult<CourseDto>(
                result.Courses.Select(ToDto).ToArray(), result.Page, result.PageSize, result.TotalCount));
        }
        catch (RpcException exception)
        {
            return ApiProblemResults.FromGrpc(httpContext, exception);
        }
    }

    private static async Task<IResult> GetAsync(
        string id,
        HttpContext httpContext,
        Capacity.CourseCapacity.CourseCapacityClient capacity,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var courseId) || courseId == Guid.Empty)
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError, "Mã lớp học phần không hợp lệ.");
        }
        try
        {
            var response = await capacity.CheckCapacityAsync(
                new Capacity.GetCourseRequest { CourseSectionId = courseId.ToString() },
                CreateHeaders(httpContext), DateTime.UtcNow.AddSeconds(3), cancellationToken);
            return Results.Ok(ToDto(response.Snapshot));
        }
        catch (RpcException exception)
        {
            return ApiProblemResults.FromGrpc(httpContext, exception);
        }
    }

    internal static Metadata CreateHeaders(HttpContext context)
    {
        var headers = new Metadata
        {
            { "x-correlation-id", CorrelationIdMiddleware.Get(context).ToString() }
        };
        return headers;
    }

    internal static CourseDto ToDto(Capacity.CourseSnapshot snapshot) => new(
        Guid.Parse(snapshot.CourseSectionId),
        snapshot.CourseCode,
        Guid.Parse(snapshot.SubjectId),
        snapshot.SubjectCode,
        snapshot.SubjectName,
        snapshot.Credits,
        Guid.Parse(snapshot.SemesterId),
        snapshot.SemesterName,
        snapshot.LecturerName,
        snapshot.Schedules.Select(schedule => new CourseScheduleDto(
            schedule.DayOfWeek, schedule.StartTime, schedule.EndTime, schedule.Room)).ToArray(),
        snapshot.Capacity,
        snapshot.RegisteredCount,
        snapshot.RemainingSeats,
        snapshot.Status.ToString(),
        snapshot.Version);
}

public sealed class CourseListQuery
{
    [FromQuery(Name = "search")]
    public string? Search { get; set; }
    [FromQuery(Name = "semesterId")]
    public string? SemesterId { get; set; }
    [FromQuery(Name = "status")]
    public string? Status { get; set; }
    [FromQuery(Name = "page")]
    public int Page { get; set; } = 1;
    [FromQuery(Name = "pageSize")]
    public int PageSize { get; set; } = 20;
}
