namespace CourseCapacity.Service.Data.Entities;

public enum CourseStatus
{
    Open,
    Closed
}

public enum RegistrationStatus
{
    Active,
    Cancelled
}

public sealed class Subject
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int Credits { get; set; }
    public bool IsActive { get; set; }
    public long Version { get; set; }
    public ICollection<CourseSection> CourseSections { get; set; } = new List<CourseSection>();
}

public sealed class Semester
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsActive { get; set; }
    public ICollection<CourseSection> CourseSections { get; set; } = new List<CourseSection>();
}

public sealed class CourseSection
{
    public Guid Id { get; set; }
    public required string CourseCode { get; set; }
    public Guid SubjectId { get; set; }
    public Guid SemesterId { get; set; }
    public required string LecturerName { get; set; }
    public int Capacity { get; set; }
    public int RegisteredCount { get; set; }
    public CourseStatus Status { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Subject Subject { get; set; } = null!;
    public Semester Semester { get; set; } = null!;
    public ICollection<CourseSchedule> Schedules { get; set; } = new List<CourseSchedule>();
    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
}

public sealed class CourseSchedule
{
    public Guid Id { get; set; }
    public Guid CourseSectionId { get; set; }
    public int DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public required string Room { get; set; }
    public CourseSection CourseSection { get; set; } = null!;
}

public sealed class Registration
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public Guid CourseSectionId { get; set; }
    public RegistrationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public CourseSection CourseSection { get; set; } = null!;
}

public sealed class ProcessedOperation
{
    public Guid ActorId { get; set; }
    public Guid OperationId { get; set; }
    public required string OperationType { get; set; }
    public required string RequestHash { get; set; }
    public required string OutcomeCode { get; set; }
    public required string ResponseJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class OutboxMessage
{
    public Guid EventId { get; set; }
    public required string EventType { get; set; }
    public Guid AggregateId { get; set; }
    public long Version { get; set; }
    public required string PayloadJson { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}
