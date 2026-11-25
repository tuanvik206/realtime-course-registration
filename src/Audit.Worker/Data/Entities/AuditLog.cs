namespace Audit.Worker.Data.Entities;

public sealed class AuditLog
{
    public Guid EventId { get; set; }
    public required string EventType { get; set; }
    public int SchemaVersion { get; set; }
    public required string CorrelationId { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? StudentId { get; set; }
    public Guid? RegistrationId { get; set; }
    public Guid CourseSectionId { get; set; }
    public int Capacity { get; set; }
    public int RegisteredCount { get; set; }
    public int RemainingSeats { get; set; }
    public required string Status { get; set; }
    public long Version { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
}
