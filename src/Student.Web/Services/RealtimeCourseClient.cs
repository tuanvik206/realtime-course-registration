using Microsoft.JSInterop;

namespace Student.Web.Services;

public sealed class RealtimeCourseClient(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly HashSet<Guid> _courseIds = [];
    private readonly string _connectionId = Guid.NewGuid().ToString("N");
    private IJSObjectReference? _module;
    private DotNetObjectReference<RealtimeCourseClient>? _self;

    public event Action<CourseAvailabilityUpdate>? CapacityUpdated;
    public event Action<Guid>? CatalogChanged;
    public event Action<string>? ConnectionStateChanged;
    public event Func<Task>? Reconnected;

    public async Task StartAsync(IEnumerable<Guid> courseIds)
    {
        _courseIds.Clear();
        _courseIds.UnionWith(courseIds.Where(id => id != Guid.Empty));
        if (_module is not null) return;

        _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/courseHub.js");
        _self = DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("startCourseHub", _connectionId, _self,
            _courseIds.Select(id => id.ToString("D")).ToArray());
    }

    public async Task SetCoursesAsync(IEnumerable<Guid> courseIds)
    {
        _courseIds.Clear();
        _courseIds.UnionWith(courseIds.Where(id => id != Guid.Empty));
        if (_module is not null)
            await _module.InvokeVoidAsync("setCourseIds", _connectionId,
                _courseIds.Select(id => id.ToString("D")).ToArray());
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
        var module = _module;
        _module = null;
        if (module is not null)
        {
            try { await module.InvokeVoidAsync("stopCourseHub", _connectionId); }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            try { await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }
        _self?.Dispose();
        _self = null;
    }
}

public sealed record CourseAvailabilityUpdate(
    Guid CourseSectionId, int Capacity, int RegisteredCount, int RemainingSeats, string Status, long Version);
