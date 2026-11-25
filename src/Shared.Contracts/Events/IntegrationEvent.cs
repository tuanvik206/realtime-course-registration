namespace RealtimeCourseRegistration.Contracts.Events;

public sealed record IntegrationEvent
{
    public Guid EventId { get; init; }
    public required string EventType { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public required string CorrelationId { get; init; }
    public Guid ActorId { get; init; }
    public Guid? RegistrationId { get; init; }
    public Guid? StudentId { get; init; }
    public Guid CourseSectionId { get; init; }
    public int Capacity { get; init; }
    public int RegisteredCount { get; init; }
    public int RemainingSeats { get; init; }
    public required string Status { get; init; }
    public long Version { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
