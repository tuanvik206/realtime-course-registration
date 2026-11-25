using Microsoft.JSInterop;

namespace Student.Web.Services;

public sealed class RealtimeCourseClient(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly HashSet<Guid> _courseIds = [];
    private IJSObjectReference? _module;
    private IJSObjectReference? _connection;
    private DotNetObjectReference<RealtimeCourseClient>? _self;

    public event Action<CourseAvailabilityUpdate>? CapacityUpdated;
    public event Action<Guid>? CatalogChanged;
    public event Action<string>? ConnectionStateChanged;
    public event Func<Task>? Reconnected;

    public async Task StartAsync(IEnumerable<Guid> courseIds, string accessToken)
    {
        _courseIds.Clear();
        _courseIds.UnionWith(courseIds.Where(id => id != Guid.Empty));
        if (_connection is not null || string.IsNullOrWhiteSpace(accessToken)) return;

        _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/courseHub.js");
        _self = DotNetObjectReference.Create(this);
        _connection = await _module.InvokeAsync<IJSObjectReference>("createCourseHub", _self, accessToken);
        await _connection.InvokeVoidAsync("start", _courseIds.Select(id => id.ToString("D")).ToArray());
    }

    public async Task SetCoursesAsync(IEnumerable<Guid> courseIds)
    {
        _courseIds.Clear();
        _courseIds.UnionWith(courseIds.Where(id => id != Guid.Empty));
        if (_connection is not null)
            await _connection.InvokeVoidAsync("setCourses", _courseIds.Select(id => id.ToString("D")).ToArray());
    }

    [JSInvokable]
    public Task OnCapacityUpdated(CourseAvailabilityUpdate update)
    {
        CapacityUpdated?.Invoke(update);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnCatalogChanged(string courseSectionId)
    {
        if (Guid.TryParse(courseSectionId, out var id)) CatalogChanged?.Invoke(id);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnConnectionStateChanged(string state)
    {
        ConnectionStateChanged?.Invoke(state);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnReconnected()
    {
        if (Reconnected is not null) await Reconnected.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            try { await _connection.InvokeVoidAsync("stop"); }
            catch (JSDisconnectedException) { }
            await _connection.DisposeAsync();
        }
        if (_module is not null) await _module.DisposeAsync();
        _self?.Dispose();
    }
}

public sealed record CourseAvailabilityUpdate(
    Guid CourseSectionId, int Capacity, int RegisteredCount, int RemainingSeats, string Status, long Version);
