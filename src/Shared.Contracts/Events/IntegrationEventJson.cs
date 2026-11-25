using System.Text.Json;

namespace RealtimeCourseRegistration.Contracts.Events;

public static class IntegrationEventJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IntegrationEvent message) => JsonSerializer.Serialize(message, Options);

    public static IntegrationEvent Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<IntegrationEvent>(json, Options)
            ?? throw new JsonException("The integration event payload was empty.");
    }
}
