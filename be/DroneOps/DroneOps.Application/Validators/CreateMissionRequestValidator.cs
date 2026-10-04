using DroneOps.Application.DTOs.Request.Missions;
using FluentValidation;

namespace DroneOps.Application.Validators.Missions;

public sealed class CreateMissionRequestValidator
    : AbstractValidator<CreateMissionRequest>
{
    private static readonly string[] AllowedActionTypes =
    [
        "TakeOff",
        "FlyThrough",
        "Hover",
        "CapturePhoto",
        "Land"
    ];

    public CreateMissionRequestValidator()
    {
        RuleFor(x => x.UavId)
            .NotEmpty()
            .WithMessage("UAV is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Mission name is required.")
            .MaximumLength(200)
            .WithMessage("Mission name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000)
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.StartTime)
            .NotEmpty()
            .WithMessage("Start time is required.");

        RuleFor(x => x.EndTime)
            .NotEmpty()
            .WithMessage("End time is required.")
            .GreaterThan(x => x.StartTime)
            .WithMessage("End time must be later than start time.");

        RuleFor(x => x.Waypoints)
            .NotNull()
            .WithMessage("Waypoints are required.")
            .Must(x => x is { Count: >= 2 })
            .WithMessage("A flight route must contain at least 2 waypoints.");

        RuleForEach(x => x.Waypoints)
            .SetValidator(new CreateWaypointRequestValidator());

        RuleFor(x => x.Waypoints)
            .Must(HaveUniqueSequenceOrders)
            .WithMessage("Waypoint sequence orders must be unique.");

        RuleFor(x => x.Waypoints)
            .Must(HaveContinuousSequenceOrders)
            .WithMessage(
                "Waypoint sequence orders must start at 1 and be continuous.");
    }

    private static bool HaveUniqueSequenceOrders(
        List<CreateWaypointRequest>? waypoints)
    {
        if (waypoints is null)
            return false;

        return waypoints
            .Select(x => x.SequenceOrder)
            .Distinct()
            .Count() == waypoints.Count;
    }

    private static bool HaveContinuousSequenceOrders(
        List<CreateWaypointRequest>? waypoints)
    {
        if (waypoints is null || waypoints.Count == 0)
            return false;

        var sequenceOrders = waypoints
            .Select(x => x.SequenceOrder)
            .OrderBy(x => x)
            .ToList();

        return sequenceOrders.SequenceEqual(
            Enumerable.Range(1, sequenceOrders.Count));
    }

    private sealed class CreateWaypointRequestValidator
        : AbstractValidator<CreateWaypointRequest>
    {
        public CreateWaypointRequestValidator()
        {
            RuleFor(x => x.SequenceOrder)
                .GreaterThan(0)
                .WithMessage("Sequence order must be greater than 0.");

            RuleFor(x => x.Latitude)
                .InclusiveBetween(-90m, 90m)
                .WithMessage("Latitude must be between -90 and 90.");

            RuleFor(x => x.Longitude)
                .InclusiveBetween(-180m, 180m)
                .WithMessage("Longitude must be between -180 and 180.");

            RuleFor(x => x.Altitude)
                .GreaterThanOrEqualTo(0)
                .When(x => x.Altitude.HasValue)
                .WithMessage("Altitude must not be negative.");

            RuleFor(x => x.ActionType)
                .Must(action => action is null ||
                                AllowedActionTypes.Contains(
                                    action,
                                    StringComparer.OrdinalIgnoreCase))
                .WithMessage(
                    "Action type must be TakeOff, FlyThrough, Hover, CapturePhoto or Land.");
        }
    }
}