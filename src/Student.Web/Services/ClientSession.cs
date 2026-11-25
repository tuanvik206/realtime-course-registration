namespace Student.Web.Services;

public sealed record ClientSession(
    string AccessToken, DateTimeOffset ExpiresAt, string Role, Guid? StudentId, string DisplayName);

public sealed class ClientSessionStore
{
    private ClientSession? _session;

    public ClientSession? Session => _session;
    public bool IsAuthenticated => _session is not null && _session.ExpiresAt > DateTimeOffset.UtcNow;

    public void SignIn(ClientSession session)
    {
        _session = session;
    }

    public void SignOut() => _session = null;
}

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class UiAuthorizeAttribute(string roles) : Attribute
{
    public string[] Roles { get; } = roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
