using RealtimeCourseRegistration.Contracts.Common;

namespace RealtimeCourseRegistration.Contracts.Events;

public sealed record EventValidationResult(bool IsValid, string? ErrorCode = null)
{
    public static EventValidationResult Valid { get; } = new(true);

    public static EventValidationResult Invalid(string errorCode) => new(false, errorCode);
}

public static class IntegrationEventValidator
{
    public static EventValidationResult Validate(IntegrationEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.EventId == Guid.Empty)
        {
            return EventValidationResult.Invalid(ErrorCodes.InvalidEventId);
        }

        if (message.SchemaVersion != 1)
        {
            return EventValidationResult.Invalid(ErrorCodes.InvalidEventSchema);
        }

        if (!IntegrationEventTypes.IsSupported(message.EventType))
        {
            return EventValidationResult.Invalid(ErrorCodes.InvalidEventType);
        }

        if (string.IsNullOrWhiteSpace(message.CorrelationId)
            || message.CorrelationId.Length > 128
            || message.ActorId == Guid.Empty
            || message.CourseSectionId == Guid.Empty
            || message.Version < 0
            || message.OccurredAt == default
            || message.OccurredAt.Offset != TimeSpan.Zero)
        {
            return EventValidationResult.Invalid(ErrorCodes.InvalidEventSchema);
        }

        if (message.Capacity <= 0
            || message.RegisteredCount < 0
            || message.RegisteredCount > message.Capacity
            || message.RemainingSeats != message.Capacity - message.RegisteredCount
            || message.Status is not ("Open" or "Closed"))
        {
            return EventValidationResult.Invalid(ErrorCodes.InvalidEventSnapshot);
        }

        if ((message.EventType is IntegrationEventTypes.RegistrationCreated or IntegrationEventTypes.RegistrationCancelled)
            && (!message.RegistrationId.HasValue
                || message.RegistrationId == Guid.Empty
                || !message.StudentId.HasValue
                || message.StudentId == Guid.Empty))
        {
            return EventValidationResult.Invalid(ErrorCodes.InvalidEventSchema);
        }

        return EventValidationResult.Valid;
    }
}
