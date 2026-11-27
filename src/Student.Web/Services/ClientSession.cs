namespace Student.Web.Services;

public sealed record ClientSession(
    DateTimeOffset ExpiresAt, string Role, Guid? StudentId, string DisplayName);

public sealed class ClientSessionStore
{
    private ClientSession? _session;
    private bool _restored;

    public ClientSession? Session => _session;
    public bool IsAuthenticated => _session is not null && _session.ExpiresAt > DateTimeOffset.UtcNow;

    public void SignIn(ClientSession session)
    {
        _session = session;
        _restored = true;
    }

    public async Task RestoreAsync(ApiClient api)
    {
        if (_restored) return;
        _restored = true;
        try
        {
            var profile = await api.GetProfileAsync();
            _session = new ClientSession(profile.ExpiresAt, profile.Role, profile.StudentId, profile.DisplayName);
        }
        catch (ApiException)
        {
            _session = null;
        }
        catch (HttpRequestException)
        {
            _session = null;
        }
        catch (OperationCanceledException)
        {
            _session = null;
        }
    }

    public void SignOut()
    {
        _session = null;
        _restored = true;
    }
}

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class UiAuthorizeAttribute(string roles) : Attribute
{
    public string[] Roles { get; } = roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
