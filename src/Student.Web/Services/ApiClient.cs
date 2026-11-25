using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Student.Web.Services;

public sealed class ApiClient(HttpClient httpClient, ClientSessionStore auth)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/auth/login",
            new LoginRequest(username, password), JsonOptions, cancellationToken);
        return await ReadAsync<LoginResponse>(response, cancellationToken);
    }

    public Task<PagedResult<CourseDto>> GetCoursesAsync(
        string? search = null, string? status = null, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = $"page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += $"&search={Uri.EscapeDataString(search)}";
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query += $"&status={Uri.EscapeDataString(status)}";
        }
        return GetAsync<PagedResult<CourseDto>>($"api/courses?{query}", cancellationToken);
    }

    public Task<CourseDto> GetCourseAsync(Guid courseSectionId, CancellationToken cancellationToken = default) =>
        GetAsync<CourseDto>($"api/courses/{courseSectionId:D}", cancellationToken);

    public Task<StudentSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        GetAsync<StudentSummaryDto>("api/students/me/summary", cancellationToken);

    public Task<IReadOnlyList<StudentScheduleDto>> GetScheduleAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<StudentScheduleDto>>("api/students/me/schedule", cancellationToken);

    public Task<AdminStatisticsDto> GetAdminStatisticsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<AdminStatisticsDto>("api/admin/statistics", cancellationToken);

    public Task<PagedResult<SubjectDto>> GetSubjectsAsync(
        string? search = null, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<SubjectDto>>($"api/subjects?search={Uri.EscapeDataString(search ?? string.Empty)}&page={page}&pageSize={pageSize}", cancellationToken);

    public Task<IReadOnlyList<SemesterDto>> GetSemestersAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<SemesterDto>>("api/semesters?activeOnly=true", cancellationToken);

    public Task<UserProfileDto> GetProfileAsync(CancellationToken cancellationToken = default) =>
        GetAsync<UserProfileDto>("api/auth/me", cancellationToken);

    public Task<PagedResult<AdminStudentDto>> GetAdminStudentsAsync(
        string? search = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<AdminStudentDto>>($"api/admin/students?search={Uri.EscapeDataString(search ?? string.Empty)}&page={page}&pageSize={pageSize}", cancellationToken);

    public Task<PagedResult<SubjectDto>> GetAdminSubjectsAsync(
        string? search = null, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<SubjectDto>>($"api/admin/subjects?search={Uri.EscapeDataString(search ?? string.Empty)}&page={page}&pageSize={pageSize}", cancellationToken);

    public Task<PagedResult<CourseDto>> GetAdminCoursesAsync(
        string? search = null, string? status = null, int page = 1, int pageSize = 100,
        CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<CourseDto>>($"api/admin/courses?search={Uri.EscapeDataString(search ?? string.Empty)}&status={Uri.EscapeDataString(status ?? string.Empty)}&page={page}&pageSize={pageSize}", cancellationToken);

    public Task<PagedResult<CourseRegistrationDto>> GetCourseRegistrationsAsync(
        Guid courseId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<CourseRegistrationDto>>($"api/admin/courses/{courseId:D}/registrations?page={page}&pageSize={pageSize}", cancellationToken);

    public Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(
        string? eventType = null, Guid? courseId = null, Guid? studentId = null,
        int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var query = $"page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(eventType)) query += $"&eventType={Uri.EscapeDataString(eventType)}";
        if (courseId.HasValue) query += $"&courseSectionId={courseId.Value:D}";
        if (studentId.HasValue) query += $"&studentId={studentId.Value:D}";
        return GetAsync<PagedResult<AuditLogDto>>($"api/admin/audit-logs?{query}", cancellationToken);
    }

    public Task<SubjectDto> CreateSubjectAsync(SubjectEditDto subject, CancellationToken cancellationToken = default) =>
        SendJsonAsync<SubjectDto>(HttpMethod.Post, "api/admin/subjects", subject, Guid.NewGuid(), cancellationToken);

    public Task<SubjectDto> UpdateSubjectAsync(
        Guid id, SubjectEditDto subject, CancellationToken cancellationToken = default) =>
        SendJsonAsync<SubjectDto>(HttpMethod.Put, $"api/admin/subjects/{id:D}", subject, Guid.NewGuid(), cancellationToken);

    public Task<CourseDto> CreateCourseAsync(CourseEditDto course, CancellationToken cancellationToken = default) =>
        SendJsonAsync<CourseDto>(HttpMethod.Post, "api/admin/courses", course, Guid.NewGuid(), cancellationToken);

    public Task<CourseDto> UpdateCourseAsync(
        Guid id, CourseEditDto course, CancellationToken cancellationToken = default) =>
        SendJsonAsync<CourseDto>(HttpMethod.Put, $"api/admin/courses/{id:D}", course, Guid.NewGuid(), cancellationToken);

    public Task<CourseDto> UpdateCourseCapacityAsync(
        Guid id, CapacityEditDto value, CancellationToken cancellationToken = default) =>
        SendJsonAsync<CourseDto>(HttpMethod.Patch, $"api/admin/courses/{id:D}/capacity", value, Guid.NewGuid(), cancellationToken);

    public Task<CourseDto> UpdateCourseStatusAsync(
        Guid id, StatusEditDto value, CancellationToken cancellationToken = default) =>
        SendJsonAsync<CourseDto>(HttpMethod.Patch, $"api/admin/courses/{id:D}/status", value, Guid.NewGuid(), cancellationToken);

    public Task<PagedResult<RegistrationDto>> GetRegistrationsAsync(
        string status = "Active", int page = 1, int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var studentId = auth.Session?.StudentId
            ?? throw new ApiException(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "Đăng nhập để xem đăng ký.");
        var url = $"api/students/{studentId:D}/registrations?status={Uri.EscapeDataString(status)}&page={page}&pageSize={pageSize}";
        return GetAsync<PagedResult<RegistrationDto>>(url, cancellationToken);
    }

    public async Task<RegistrationResultDto> RegisterAsync(
        Guid courseSectionId, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/registrations");
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
        request.Content = JsonContent.Create(new RegistrationRequest(courseSectionId), options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadAsync<RegistrationResultDto>(response, cancellationToken);
    }

    public async Task CancelAsync(
        Guid registrationId, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"api/registrations/{registrationId:D}");
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return;
        }
        await ThrowAsync(response, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object payload,
        Guid idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path);
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString("D"));
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (auth.IsAuthenticated && auth.Session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
        }
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            await ThrowAsync(response, cancellationToken);
        }
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new ApiException(response.StatusCode, "INVALID_RESPONSE", "Máy chủ trả dữ liệu không hợp lệ.");
    }

    private static async Task ThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var code = root.TryGetProperty("errorCode", out var errorCode) ? errorCode.GetString() : null;
            var detail = root.TryGetProperty("detail", out var errorDetail) ? errorDetail.GetString() : null;
            var title = root.TryGetProperty("title", out var errorTitle) ? errorTitle.GetString() : null;
            throw new ApiException(response.StatusCode, code ?? "REQUEST_FAILED", detail ?? title ?? "Yêu cầu không thể hoàn tất.");
        }
        catch (JsonException)
        {
            throw new ApiException(response.StatusCode, "REQUEST_FAILED", "Yêu cầu không thể hoàn tất.");
        }
    }
}

public sealed class ApiException(HttpStatusCode statusCode, string errorCode, string message)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string Role, Guid? StudentId, string DisplayName);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);
public sealed record CourseScheduleDto(int DayOfWeek, string StartTime, string EndTime, string Room);
public sealed record CourseDto(
    Guid Id, string CourseCode, Guid SubjectId, string SubjectCode, string SubjectName, int Credits, Guid SemesterId,
    string SemesterName, string LecturerName, IReadOnlyList<CourseScheduleDto> Schedules, int Capacity,
    int RegisteredCount, int RemainingSeats, string Status, long Version);
public sealed record RegistrationRequest(Guid CourseSectionId);
public sealed record RegistrationResultDto(
    Guid RegistrationId, Guid StudentId, Guid CourseSectionId, string Status, int Capacity,
    int RegisteredCount, int RemainingSeats, long Version, DateTimeOffset CreatedAt);
public sealed record RegistrationDto(
    Guid RegistrationId, Guid StudentId, Guid CourseSectionId, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? CancelledAt, CourseDto Course);
public sealed record StudentSummaryDto(int TotalActiveRegistrations, int TotalCredits);
public sealed record StudentScheduleDto(
    string CourseSectionId, string CourseCode, int DayOfWeek, string StartTime, string EndTime, string Room);
public sealed record SubjectDto(Guid Id, string Code, string Name, int Credits, bool IsActive, long Version);
public sealed record AdminStatisticsDto(long TotalStudents, long TotalCourses, long ActiveRegistrations, long FullCourses);
public sealed record SemesterDto(Guid Id, string Code, string Name, string StartDate, string EndDate, bool IsActive);
public sealed record UserProfileDto(Guid UserId, string Username, string Role, Guid? StudentId, string DisplayName);
public sealed record AdminStudentDto(Guid StudentId, string StudentCode, string FullName,
    string? Email, string? Major, string Username, bool IsActive);
public sealed record CourseRegistrationDto(Guid RegistrationId, Guid StudentId, string? StudentCode,
    string? FullName, string? Email, string? Major, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt, CourseDto Course);
public sealed record AuditLogDto(Guid EventId, string EventType, int SchemaVersion, string CorrelationId,
    Guid? ActorId, Guid? StudentId, Guid? RegistrationId, Guid CourseSectionId, int Capacity,
    int RegisteredCount, int RemainingSeats, string Status, long Version,
    DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt);
public sealed record SubjectEditDto(string Code, string Name, int Credits, bool IsActive, long ExpectedVersion = 0);
public sealed record CourseEditDto(string CourseCode, Guid SubjectId, Guid SemesterId, string LecturerName,
    int Capacity, long ExpectedVersion, IReadOnlyList<ScheduleEditDto> Schedules);
public sealed record ScheduleEditDto(int DayOfWeek, string StartTime, string EndTime, string Room);
public sealed record CapacityEditDto(int Capacity, long ExpectedVersion);
public sealed record StatusEditDto(string Status, long ExpectedVersion);
