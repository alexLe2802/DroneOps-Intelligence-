using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.Missions;

public static class PermitNotifications
{
    public static Notification For(Mission mission, Guid recipient, Guid actor, string title, string message) => new()
    {
        Id = Guid.NewGuid(), RecipientUserId = recipient, ActorUserId = actor, Type = "MissionPermit",
        Title = title, Message = $"{mission.Name}: {message}", ReferenceId = mission.Id,
        ReferenceType = "Mission", ActionUrl = $"/missions/{mission.Id}"
    };
}
