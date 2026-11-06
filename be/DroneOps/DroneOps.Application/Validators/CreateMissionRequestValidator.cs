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
        "Land"
    ];

    public CreateMissionRequestValidator()
    {
        RuleFor(x => x).Custom((x, context) => {
            foreach (var point in x.Waypoints ?? [])
                if (point.Navigation?.MissionFences is { } fences && (point.SequenceOrder != 1 || fences.Count > 20
                    || fences.Any(f => !DroneOps.Application.Services.Missions.MissionFenceGeometry.Valid(f))
                    || fences.Select(f => f?.Id).Distinct().Count() != fences.Count))
                    context.AddFailure("Waypoints.Navigation.MissionFences", "Mission fences belong to waypoint 1; use up to 20 valid, uniquely identified polygons or circles.");
            foreach (var point in x.Waypoints ?? [])
                if (point.Navigation?.PlannedHome is { } home && (point.SequenceOrder != 1
                    || home.Latitude is < -90 or > 90 || home.Longitude is < -180 or > 180
                    || home.AltitudeMslMeters is < -500 or > 10000))
                    context.AddFailure("Waypoints.Navigation.PlannedHome", "Planned Home must be on waypoint 1 with valid WGS84 coordinates and optional MSL elevation (-500 to 10000 m).");
            foreach (var error in DroneOps.Application.Services.Missions.AutomaticFlightPolicy.Errors(
                x.AutomaticFlightRequested, x.AltitudeReference, x.AverageSpeedMetersPerSecond, x.MaxSpeedMetersPerSecond,
                x.AutomaticFlightLimits ?? new DroneOps.Domain.Entities.AutomaticFlightLimits(), (x.Waypoints ?? []).Select(w => (w.Altitude, w.ActionType))))
                context.AddFailure("AutomaticFlightLimits", error);
            if (x.AutomaticFlightRequested)
            {
                foreach (var error in DroneOps.Application.Services.Missions.AutomaticFlightPolicy.NavigationErrors(
                    (x.Waypoints ?? []).Select(w => (w.Navigation, w.ActionType)), x.MaxSpeedMetersPerSecond))
                    context.AddFailure("Waypoints.Navigation", error);
                if (x.AutomaticFlightLimits is { } limits)
                    foreach (var error in DroneOps.Application.Services.Missions.RecoveryEnergyCalculator.ProfileErrors(
                        limits.RecoveryEnergyProfile, limits, DateTimeOffset.UtcNow,
                        new DateTimeOffset(DateTime.SpecifyKind(x.EndTime, DateTimeKind.Utc)), x.UavId))
                        context.AddFailure("AutomaticFlightLimits.RecoveryEnergyProfile", error);
                if ((x.Waypoints ?? []).Any(w => w.Navigation?.LandingSiteSurvey is { } survey
                    && survey.ValidUntilUtc.UtcDateTime < x.EndTime))
                    context.AddFailure("Waypoints.Navigation.LandingSiteSurvey", "Landing-site inspections must remain valid through the mission end.");
            }
        });
        RuleFor(x=>x.AltitudeReference).Must(x=>x is null or "AGL" or "MSL" or "Relative").WithMessage("Altitude reference must be AGL, MSL or Relative.");
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
        RuleFor(x => x.AverageSpeedMetersPerSecond)
    .GreaterThan(0)
    .WithMessage(
        "Average speed must be greater than 0 m/s.");

        RuleFor(x => x.MaxSpeedMetersPerSecond)
            .GreaterThan(0)
            .WithMessage(
                "Maximum speed must be greater than 0 m/s.");

        RuleFor(x => x)
            .Must(x =>
                x.AverageSpeedMetersPerSecond <=
                x.MaxSpeedMetersPerSecond)
            .When(x =>
                x.AverageSpeedMetersPerSecond > 0 &&
                x.MaxSpeedMetersPerSecond > 0)
            .WithMessage(
                "Average speed cannot exceed maximum speed.");
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
                    "Action type must be TakeOff, FlyThrough, Hover or Land. Camera actions are deferred.");
        }
    }
}
