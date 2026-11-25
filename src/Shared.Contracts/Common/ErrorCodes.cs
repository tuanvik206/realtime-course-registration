using RealtimeCourseRegistration.Contracts.Capacity.V1;

namespace RealtimeCourseRegistration.Contracts.Common;

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidIdempotencyKey = "INVALID_IDEMPOTENCY_KEY";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Forbidden = "FORBIDDEN";
    public const string CourseNotFound = "COURSE_NOT_FOUND";
    public const string RegistrationNotFound = "REGISTRATION_NOT_FOUND";
    public const string StudentNotFound = "STUDENT_NOT_FOUND";
    public const string AlreadyRegistered = "ALREADY_REGISTERED";
    public const string CourseFull = "COURSE_FULL";
    public const string CourseClosed = "COURSE_CLOSED";
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string InvalidCapacity = "INVALID_CAPACITY";
    public const string RateLimited = "RATE_LIMITED";
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";
    public const string OperationOutcomeUnknown = "OPERATION_OUTCOME_UNKNOWN";
    public const string InternalError = "INTERNAL_ERROR";
    public const string InvalidEventId = "INVALID_EVENT_ID";
    public const string InvalidEventType = "INVALID_EVENT_TYPE";
    public const string InvalidEventSchema = "INVALID_EVENT_SCHEMA";
    public const string InvalidEventSnapshot = "INVALID_EVENT_SNAPSHOT";
}

public static class OutcomeErrorCodeMap
{
    public static string? ToErrorCode(OperationOutcome outcome) => outcome switch
    {
        OperationOutcome.Succeeded => null,
        OperationOutcome.AlreadyRegistered => ErrorCodes.AlreadyRegistered,
        OperationOutcome.CourseFull => ErrorCodes.CourseFull,
        OperationOutcome.CourseClosed => ErrorCodes.CourseClosed,
        OperationOutcome.CourseNotFound => ErrorCodes.CourseNotFound,
        OperationOutcome.RegistrationNotFound => ErrorCodes.RegistrationNotFound,
        OperationOutcome.StudentNotFound => ErrorCodes.StudentNotFound,
        OperationOutcome.Forbidden => ErrorCodes.Forbidden,
        OperationOutcome.VersionConflict => ErrorCodes.VersionConflict,
        OperationOutcome.IdempotencyConflict => ErrorCodes.IdempotencyConflict,
        OperationOutcome.InvalidCapacity => ErrorCodes.InvalidCapacity,
        OperationOutcome.ValidationError => ErrorCodes.ValidationError,
        _ => ErrorCodes.InternalError
    };
}
