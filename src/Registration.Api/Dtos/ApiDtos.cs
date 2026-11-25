using System.Text.Json.Serialization;

namespace Registration.Api.Dtos;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);

public sealed record CourseScheduleDto(int DayOfWeek, string StartTime, string EndTime, string Room);

public sealed record CourseDto(
    Guid Id,
    string CourseCode,
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    int Credits,
    Guid SemesterId,
    string SemesterName,
    string LecturerName,
    IReadOnlyList<CourseScheduleDto> Schedules,
    int Capacity,
    int RegisteredCount,
    int RemainingSeats,
    string Status,
    long Version);

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string Role,
    Guid? StudentId,
    string DisplayName);

public sealed record UserProfileDto(
    Guid UserId,
    string Username,
    string Role,
    Guid? StudentId,
    string DisplayName);

public sealed record RegistrationRequest(Guid CourseSectionId);

public sealed record RegistrationResultDto(
    Guid RegistrationId,
    Guid StudentId,
    Guid CourseSectionId,
    string Status,
    int Capacity,
    int RegisteredCount,
    int RemainingSeats,
    long Version,
    DateTimeOffset CreatedAt);

public sealed record RegistrationDto(
    Guid RegistrationId,
    Guid StudentId,
    Guid CourseSectionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    CourseDto Course);

public sealed record StudentSummaryDto(int TotalActiveRegistrations, int TotalCredits);

public sealed record IdempotencyKeyRequest([property: JsonIgnore] string Value);
