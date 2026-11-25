using Google.Protobuf;
using System.Text.Json;
using RealtimeCourseRegistration.Contracts.Capacity.V1;
using RealtimeCourseRegistration.Contracts.Events;

namespace UnitTests;

public sealed class AdminContractTests
{
    [Theory]
    [InlineData(IntegrationEventTypes.CourseCapacityChanged)]
    [InlineData(IntegrationEventTypes.CourseStatusChanged)]
    [InlineData(IntegrationEventTypes.CourseCreated)]
    [InlineData(IntegrationEventTypes.CourseUpdated)]
    public void AdminCourseEventsAreSupportedAndPrivacySafe(string eventType)
    {
        var message = new IntegrationEvent
        {
            EventId = Guid.NewGuid(), EventType = eventType, CorrelationId = Guid.NewGuid().ToString(),
            ActorId = Guid.NewGuid(), CourseSectionId = Guid.NewGuid(), Capacity = 25,
            RegisteredCount = 7, RemainingSeats = 18, Status = "Open", Version = 2,
            OccurredAt = DateTimeOffset.UtcNow
        };

        Assert.True(IntegrationEventValidator.Validate(message).IsValid);
        var json = IntegrationEventJson.Serialize(message);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("studentId").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("registrationId").ValueKind);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdminCapacityRequestCarriesExpectedVersionAndOperationId()
    {
        var actorId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var request = new UpdateCapacityRequest
        {
            ActorId = actorId.ToString(), OperationId = operationId.ToString(),
            CourseSectionId = courseId.ToString(), Capacity = 12, ExpectedVersion = 42
        };

        var actual = UpdateCapacityRequest.Parser.ParseFrom(request.ToByteArray());

        Assert.Equal(actorId.ToString(), actual.ActorId);
        Assert.Equal(operationId.ToString(), actual.OperationId);
        Assert.Equal(courseId.ToString(), actual.CourseSectionId);
        Assert.Equal(42, actual.ExpectedVersion);
    }
}
