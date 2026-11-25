namespace RealtimeCourseRegistration.Contracts.Events;

public static class IntegrationEventTypes
{
    public const string RegistrationCreated = "RegistrationCreated";
    public const string RegistrationCancelled = "RegistrationCancelled";
    public const string CourseCapacityChanged = "CourseCapacityChanged";
    public const string CourseStatusChanged = "CourseStatusChanged";
    public const string CourseCreated = "CourseCreated";
    public const string CourseUpdated = "CourseUpdated";

    public static string? GetRoutingKey(string eventType) => eventType switch
    {
        RegistrationCreated => "registration.created",
        RegistrationCancelled => "registration.cancelled",
        CourseCapacityChanged => "course.capacity.changed",
        CourseStatusChanged => "course.status.changed",
        CourseCreated => "course.created",
        CourseUpdated => "course.updated",
        _ => null
    };

    public static bool IsSupported(string eventType) => GetRoutingKey(eventType) is not null;
}
