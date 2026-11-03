namespace DroneOps.Application.Validators;

public static class UavSpecificationValidator
{
    public static void Validate(
        decimal? emptyWeightKg,
        decimal? maxTakeoffWeightKg,
        decimal? maxPayloadKg,
        int? batteryCapacityMah,
        decimal? batteryVoltage,
        int? batteryCellCount,
        int? maxFlightTimeMinutes,
        decimal? maxRangeKm,
        decimal? maxSpeedKmh,
        decimal? maxWindSpeedMps,
        decimal? maxOperatingAltitudeMeters)
    {
        if (emptyWeightKg is <= 0)
            throw new ArgumentException("Empty weight must be greater than 0.");

        if (maxTakeoffWeightKg is <= 0)
            throw new ArgumentException("Maximum takeoff weight must be greater than 0.");

        if (maxPayloadKg is < 0)
            throw new ArgumentException("Maximum payload cannot be negative.");

        if (emptyWeightKg.HasValue &&
            maxTakeoffWeightKg.HasValue &&
            maxTakeoffWeightKg < emptyWeightKg)
        {
            throw new ArgumentException(
                "Maximum takeoff weight cannot be less than empty weight.");
        }

        if (emptyWeightKg.HasValue &&
            maxPayloadKg.HasValue &&
            maxTakeoffWeightKg.HasValue &&
            emptyWeightKg.Value + maxPayloadKg.Value > maxTakeoffWeightKg.Value)
        {
            throw new ArgumentException(
                "Empty weight plus maximum payload cannot exceed maximum takeoff weight.");
        }

        if (batteryCapacityMah is <= 0)
            throw new ArgumentException("Battery capacity must be greater than 0.");

        if (batteryVoltage is <= 0)
            throw new ArgumentException("Battery voltage must be greater than 0.");

        if (batteryCellCount is <= 0)
            throw new ArgumentException("Battery cell count must be greater than 0.");

        if (maxFlightTimeMinutes is <= 0)
            throw new ArgumentException("Maximum flight time must be greater than 0.");

        if (maxRangeKm is <= 0)
            throw new ArgumentException("Maximum range must be greater than 0.");

        if (maxSpeedKmh is <= 0)
            throw new ArgumentException("Maximum speed must be greater than 0.");

        if (maxWindSpeedMps is <= 0)
            throw new ArgumentException("Maximum wind speed must be greater than 0.");

        if (maxOperatingAltitudeMeters is <= 0)
            throw new ArgumentException(
                "Maximum operating altitude must be greater than 0.");
    }
}