using DroneOps.Application.DTOs.Request.Geofences;
using FluentValidation;

namespace DroneOps.Application.Validators.Geofences;

public class GeofenceCoordinateRequestValidator
    : AbstractValidator<GeofenceCoordinateRequest>
{
    public GeofenceCoordinateRequestValidator()
    {
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90m, 90m)
            .WithMessage("Latitude must be between -90 and 90.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180m, 180m)
            .WithMessage("Longitude must be between -180 and 180.");
    }
}