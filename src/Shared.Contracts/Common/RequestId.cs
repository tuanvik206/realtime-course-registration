namespace RealtimeCourseRegistration.Contracts.Common;

public static class RequestId
{
    public static bool TryParse(string? value, out Guid id)
    {
        return Guid.TryParseExact(value, "D", out id) && id != Guid.Empty;
    }
}
