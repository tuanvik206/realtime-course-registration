namespace RealtimeCourseRegistration.Contracts.Events;

public static class BrokerTopology
{
    public const string Exchange = "registration.events";
    public const string DeadLetterExchange = "registration.dlx";
    public const string NotificationQueue = "notification.queue";
    public const string AuditQueue = "audit.queue";
    public const string NotificationDeadLetterQueue = "notification.deadletter";
    public const string AuditDeadLetterQueue = "audit.deadletter";
    public const string RegistrationBinding = "registration.*";
    public const string CourseBinding = "course.#";
}
