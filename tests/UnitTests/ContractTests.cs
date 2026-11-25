using Google.Protobuf;
using RealtimeCourseRegistration.Contracts.Capacity.V1;
using RealtimeCourseRegistration.Contracts.Common;
using RealtimeCourseRegistration.Contracts.Events;
using Shared.Contracts;

namespace UnitTests;

public sealed class ContractTests
{
    [Fact]
    public void CapacitySnapshot_RoundTripsThroughProtobuf()
    {
        var snapshot = new CourseSnapshot
        {
            CourseSectionId = "c4c7f12a-770c-4e43-bfa4-c3d54cb22d4a",
            CourseCode = "DISTRIBUTED-01",
            Capacity = 20,
            RegisteredCount = 7,
            RemainingSeats = 13,
            Status = CourseStatus.Open,
            Version = 9
        };

        var actual = CourseSnapshot.Parser.ParseFrom(snapshot.ToByteArray());

        Assert.Equal(snapshot.CourseSectionId, actual.CourseSectionId);
        Assert.Equal(snapshot.RemainingSeats, actual.RemainingSeats);
        Assert.Equal(snapshot.Version, actual.Version);
    }

    [Fact]
    public void IntegrationEvent_UsesCamelCaseAndRoundTrips()
    {
        var message = ValidRegistrationEvent();

        var json = IntegrationEventJson.Serialize(message);
        var actual = IntegrationEventJson.Deserialize(json);

        Assert.Contains("\"eventType\":\"RegistrationCreated\"", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(message, actual);
    }

    [Fact]
    public void EventValidator_RejectsInconsistentSeatSnapshot()
    {
        var message = ValidRegistrationEvent() with { RemainingSeats = 5 };

        var result = IntegrationEventValidator.Validate(message);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.InvalidEventSnapshot, result.ErrorCode);
    }

    [Theory]
    [InlineData("c4c7f12a-770c-4e43-bfa4-c3d54cb22d4a", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("not-a-guid", false)]
    [InlineData("c4c7f12a770c4e43bfa4c3d54cb22d4a", false)]
    public void RequestId_RequiresNonEmptyDashedGuid(string value, bool expected)
    {
        Assert.Equal(expected, RequestId.TryParse(value, out _));
    }

    [Theory]
    [InlineData(OperationOutcome.AlreadyRegistered, ErrorCodes.AlreadyRegistered)]
    [InlineData(OperationOutcome.CourseFull, ErrorCodes.CourseFull)]
    [InlineData(OperationOutcome.IdempotencyConflict, ErrorCodes.IdempotencyConflict)]
    public void OutcomeMapsToStableErrorCode(OperationOutcome outcome, string expected)
    {
        Assert.Equal(expected, OutcomeErrorCodeMap.ToErrorCode(outcome));
    }

    private static IntegrationEvent ValidRegistrationEvent() => new()
    {
        EventId = Guid.Parse("3dcced2f-7179-41b9-b0e4-cf5f120c1a39"),
        EventType = IntegrationEventTypes.RegistrationCreated,
        SchemaVersion = ContractConstants.EventSchemaVersion,
        CorrelationId = "request-123",
        ActorId = Guid.Parse("a1c0d3c8-ed91-49ec-9d9e-f8807db929a1"),
        RegistrationId = Guid.Parse("80cc5fb9-9d20-4f9f-9e62-91c7186dc812"),
        StudentId = Guid.Parse("d79f5d55-c070-42f5-85a2-0bbb5a03a24f"),
        CourseSectionId = Guid.Parse("c4c7f12a-770c-4e43-bfa4-c3d54cb22d4a"),
        Capacity = 20,
        RegisteredCount = 7,
        RemainingSeats = 13,
        Status = "Open",
        Version = 9,
        OccurredAt = DateTimeOffset.Parse("2026-11-25T01:00:00Z")
    };
}
