using DroneOps.Application.DTOs.Request.Geofences;
using FluentValidation;

namespace DroneOps.Application.Validators.Geofences;

public class UpdateGeofenceRequestValidator
    : AbstractValidator<UpdateGeofenceRequest>
{
    public UpdateGeofenceRequestValidator()
    {
        RuleFor(x=>x.VerticalRule).Must(x=>x is "Unspecified" or "AllAltitudes" or "Band");
        RuleFor(x=>x).Must(x=>x.VerticalRule!="Band"||x.MinAltitudeMeters.HasValue||x.MaxAltitudeMeters.HasValue).WithMessage("Band requires at least one altitude bound.");
        RuleFor(x=>x.AltitudeReference).Must(x=>x is null or "AGL" or "MSL" or "Relative");
        RuleFor(x=>x).Must(x=>!x.MinAltitudeMeters.HasValue||!x.MaxAltitudeMeters.HasValue||x.MinAltitudeMeters<=x.MaxAltitudeMeters).WithMessage("Minimum altitude must not exceed maximum altitude.");
        RuleFor(x=>x.AltitudeReference).NotEmpty().When(x=>x.MinAltitudeMeters.HasValue||x.MaxAltitudeMeters.HasValue).WithMessage("Bounded airspace requires an altitude reference.");
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