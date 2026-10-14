using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DroneOps.Application.DTOs.Request.Telemetry;
using FluentValidation;

namespace DroneOps.Application.Validators.Telemetry;

public class IngestTelemetrySampleRequestValidator : AbstractValidator<IngestTelemetrySampleRequest>
{
    private static readonly string[] AllowedAltitudeReferences = { "Relative", "MSL", "AGL" };

    public IngestTelemetrySampleRequestValidator()
    {
        RuleFor(x => x.SourceId)
            .NotEmpty().WithMessage("SourceId is required.")
            .MaximumLength(100).WithMessage("SourceId cannot exceed 100 characters.");

        RuleFor(x => x.UavId)
            .NotEmpty().WithMessage("UavId is required and must not be empty.");

        RuleFor(x => x.SourceTimestamp)
            .NotEmpty().WithMessage("SourceTimestamp is required.");

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90.0m, 90.0m)
            .WithMessage("Latitude must be between -90 and 90 degrees.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180.0m, 180.0m)
            .WithMessage("Longitude must be between -180 and 180 degrees.");

        RuleFor(x => x.BatteryPercent)
            .InclusiveBetween(0, 100)
            .When(x => x.BatteryPercent.HasValue)
            .WithMessage("BatteryPercent must be between 0 and 100 when provided.");

        RuleFor(x => x.GroundSpeedMetersPerSecond)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.GroundSpeedMetersPerSecond.HasValue)
            .WithMessage("GroundSpeed must be non-negative.");

        RuleFor(x => x.AltitudeReference)
            .Must(x => AllowedAltitudeReferences.Contains(x))
            .When(x => !string.IsNullOrEmpty(x.AltitudeReference))
            .WithMessage("AltitudeReference must be 'Relative', 'MSL', or 'AGL'.");
    }
}
