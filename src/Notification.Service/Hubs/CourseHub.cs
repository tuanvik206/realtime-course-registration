using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Notification.Service.Hubs;

[Authorize]
public sealed class CourseHub : Hub
{
    private const int MaxGroupsPerConnection = 100;
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> Subscriptions = new();

    public async Task SubscribeCourse(string courseSectionId)
    {
        if (!Guid.TryParse(courseSectionId, out var courseId) || courseId == Guid.Empty)
        {
            throw new HubException("INVALID_COURSE_ID");
        }

        var courses = Subscriptions.GetOrAdd(Context.ConnectionId, _ => new ConcurrentDictionary<Guid, byte>());
        if (!courses.ContainsKey(courseId) && courses.Count >= MaxGroupsPerConnection)
        {
            throw new HubException("SUBSCRIPTION_LIMIT_REACHED");
        }
        courses.TryAdd(courseId, 0);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(courseId));
    }

    public async Task UnsubscribeCourse(string courseSectionId)
    {
        if (!Guid.TryParse(courseSectionId, out var courseId) || courseId == Guid.Empty)
        {
            throw new HubException("INVALID_COURSE_ID");
        }
        if (Subscriptions.TryGetValue(Context.ConnectionId, out var courses))
        {
            courses.TryRemove(courseId, out _);
            if (courses.IsEmpty)
            {
                Subscriptions.TryRemove(Context.ConnectionId, out _);
            }
        }
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(courseId));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Subscriptions.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    public static string GroupName(Guid courseSectionId) => $"course:{courseSectionId:D}";
}
