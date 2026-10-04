using DroneOps.Application.DTOs.Request.Geofences;
using FluentValidation;

namespace DroneOps.Application.Validators.Geofences;

public class CreateGeofenceRequestValidator
    : AbstractValidator<CreateGeofenceRequest>
{
    public CreateGeofenceRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Geofence name is required.")
            .MaximumLength(100)
            .WithMessage("Geofence name must not exceed 100 characters.");

        RuleFor(x => x.Coordinates)
            .NotNull()
            .WithMessage("Coordinates are required.")
            .Must(x => x != null && x.Count >= 3)
            .WithMessage(
                "A geofence must contain at least 3 coordinates.");

        RuleFor(x => x.Coordinates)
            .Must(HaveAtLeastThreeDistinctPoints)
            .When(x => x.Coordinates != null)
            .WithMessage(
                "A geofence must contain at least 3 distinct coordinates.");

        RuleForEach(x => x.Coordinates)
            .SetValidator(new GeofenceCoordinateRequestValidator());
    }

    private static bool HaveAtLeastThreeDistinctPoints(
        List<GeofenceCoordinateRequest> coordinates)
    {
        return coordinates
            .Select(x => new
            {
                x.Latitude,
                x.Longitude
            })
            .Distinct()
            .Count() >= 3;
    }
}