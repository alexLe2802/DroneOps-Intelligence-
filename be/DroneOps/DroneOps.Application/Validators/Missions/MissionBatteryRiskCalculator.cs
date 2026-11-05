using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Validators.Missions;

public static class MissionBatteryRiskCalculator
{
    private const decimal SafetyReservePercent = 20m;

    private const decimal ReviewThresholdPercent = 85m;

    public static BatteryRiskResult Calculate(
        Mission mission,
        UAV uav,
        WeatherAdvisory? weather)
    {
        var result = new BatteryRiskResult
        {
            SafetyReservePercent = SafetyReservePercent,
            BatteryCapacityMah = uav.BatteryCapacityMah,
            BatteryVoltage = uav.BatteryVoltage
        };

        if (uav.BatteryCapacityMah.HasValue &&
            uav.BatteryVoltage.HasValue)
        {
            result.BatteryEnergyWh = decimal.Round(
                uav.BatteryCapacityMah.Value / 1000m *
                uav.BatteryVoltage.Value,
                2,
                MidpointRounding.AwayFromZero);
        }

        var orderedWaypoints = mission.Waypoints
            .OrderBy(x => x.SequenceOrder)
            .ToList();

        if (orderedWaypoints.Count < 2)
        {
            result.Warnings.Add(
                "Battery requirement cannot be calculated because the mission route requires at least two waypoints.");

            result.Message =
                "Battery requirement is unavailable because the route is incomplete.";

            return result;
        }

        if (!mission.AverageSpeedMetersPerSecond.HasValue ||
            mission.AverageSpeedMetersPerSecond.Value <= 0)
        {
            result.Warnings.Add(
                "Battery requirement cannot be calculated because the mission average speed is missing or invalid.");

            result.Message =
                "Battery requirement is unavailable because the average flight speed is invalid.";

            return result;
        }

        if (!uav.MaxFlightTimeMinutes.HasValue ||
            uav.MaxFlightTimeMinutes.Value <= 0)
        {
            result.Warnings.Add(
                "Battery requirement cannot be calculated because the UAV maximum flight time is not configured.");

            result.Message =
                "Battery requirement is unavailable because the UAV flight-time specification is missing.";

            return result;
        }

        result.RouteDistanceMeters =
            CalculateRouteDistanceMeters(orderedWaypoints);

        var estimatedFlightSeconds =
            (decimal)result.RouteDistanceMeters /
            mission.AverageSpeedMetersPerSecond.Value;
        if (mission.AutomaticFlightRequested && mission.AutomaticFlightLimits is { } limits
            && limits.TakeoffSpeedMetersPerSecond > 0 && limits.ClimbSpeedMetersPerSecond > 0
            && limits.DescentSpeedMetersPerSecond > 0 && limits.LandingSpeedMetersPerSecond > 0)
        {
            estimatedFlightSeconds = 0;
            for (var index = 1; index < orderedWaypoints.Count; index++)
            {
                var previous = orderedWaypoints[index - 1]; var current = orderedWaypoints[index];
                var speed = current.Navigation?.SpeedMetersPerSecond ?? mission.AverageSpeedMetersPerSecond.Value;
                if (speed > 0) estimatedFlightSeconds += (decimal)HaversineDistanceMeters(previous.Latitude, previous.Longitude, current.Latitude, current.Longitude) / speed;
                var delta = (current.Altitude ?? 0) - (previous.Altitude ?? 0);
                estimatedFlightSeconds += delta >= 0 ? delta / limits.ClimbSpeedMetersPerSecond : -delta / Math.Min(limits.DescentSpeedMetersPerSecond, limits.LandingSpeedMetersPerSecond);
            }
            estimatedFlightSeconds += Math.Max(0, orderedWaypoints[0].Altitude ?? 0) / limits.TakeoffSpeedMetersPerSecond;
            var landingGround = orderedWaypoints[^1].Navigation?.LandingGroundAltitudeMeters ?? 0;
            estimatedFlightSeconds += Math.Max(0, (orderedWaypoints[^1].Altitude ?? 0) - landingGround) / limits.LandingSpeedMetersPerSecond;
            estimatedFlightSeconds += orderedWaypoints.Sum(w => Math.Max(0, w.Navigation?.HoldTimeSeconds ?? 0));
            result.Reasons.Add("Automatic estimate conservatively adds horizontal travel, vertical travel, takeoff, landing and entered holds. Unknown landing ground uses Home level for estimation only, not landing clearance.");
            result.Warnings.Add("This remains an uncalibrated endurance estimate; it does not verify emergency-return energy, aircraft power or landing safety.");
        }

        result.EstimatedFlightMinutes = decimal.Round(
            estimatedFlightSeconds / 60m,
            2,
            MidpointRounding.AwayFromZero);

        result.UavMaxFlightTimeMinutes =
            uav.MaxFlightTimeMinutes.Value;

        result.BaseBatteryPercent = decimal.Round(
            result.EstimatedFlightMinutes /
            uav.MaxFlightTimeMinutes.Value *
            100m,
            2,
            MidpointRounding.AwayFromZero);

        result.Reasons.Add(
            $"The approximately {result.RouteDistanceMeters:0} m route " +
            $"at {mission.AverageSpeedMetersPerSecond.Value:0.##} m/s " +
            $"requires an estimated {result.EstimatedFlightMinutes:0.##} minutes of flight time.");

        result.Reasons.Add(
            $"The estimated route consumes approximately " +
            $"{result.BaseBatteryPercent:0.##}% of the UAV's configured " +
            $"{uav.MaxFlightTimeMinutes.Value}-minute maximum flight time.");

        result.WeatherMultiplier =
            CalculateWeatherMultiplier(
                uav,
                weather,
                result.Reasons);

        var weatherAdjustedBatteryPercent =
            result.BaseBatteryPercent *
            result.WeatherMultiplier;

        result.WeatherAdjustmentPercent = decimal.Round(
            weatherAdjustedBatteryPercent -
            result.BaseBatteryPercent,
            2,
            MidpointRounding.AwayFromZero);

        result.RequiredBatteryPercent = decimal.Round(
            weatherAdjustedBatteryPercent +
            SafetyReservePercent,
            2,
            MidpointRounding.AwayFromZero);

        result.Reasons.Add(
            $"A {SafetyReservePercent:0}% battery safety reserve is included.");

        result.Status = "Available";

        if (result.RequiredBatteryPercent > 100m)
        {
            result.RiskLevel = "High";
            result.RequiresConfirmation = true;

            result.Message =
                $"High battery risk. The mission requires approximately " +
                $"{result.RequiredBatteryPercent:0.##}% battery for safe operation, " +
                $"which exceeds the UAV battery capacity. Pilot confirmation is required.";

            result.Warnings.Add(result.Message);

            return result;
        }

        if (result.RequiredBatteryPercent >= ReviewThresholdPercent)
        {
            result.RiskLevel = "Review";
            result.RequiresConfirmation = false;

            result.Message =
                $"Ensure the UAV has at least " +
                $"{Math.Ceiling(result.RequiredBatteryPercent):0}% battery " +
                $"before departure. The mission operates close to the " +
                $"recommended battery limit.";

            result.Warnings.Add(result.Message);

            return result;
        }

        result.RiskLevel = "Low";
        result.RequiresConfirmation = false;

        result.Message =
            $"Ensure the UAV has at least " +
            $"{Math.Ceiling(result.RequiredBatteryPercent):0}% battery " +
            $"before departure.";

        return result;
    }

    private static decimal CalculateWeatherMultiplier(
        UAV uav,
        WeatherAdvisory? weather,
        List<string> reasons)
    {
        if (weather is null ||
            string.Equals(
                weather.Status,
                "Unavailable",
                StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(
                "Weather consumption adjustment could not be applied because weather data is unavailable.");

            return 1m;
        }

        var multiplier = 1m;

        var maxPositiveHeadwind = weather.Waypoints
            .Where(x =>
                x.HeadwindMetersPerSecond.HasValue &&
                x.HeadwindMetersPerSecond.Value > 0)
            .Select(x => x.HeadwindMetersPerSecond!.Value)
            .DefaultIfEmpty(0m)
            .Max();

        if (uav.MaxWindSpeedMps.HasValue &&
            uav.MaxWindSpeedMps.Value > 0)
        {
            var headwindRatio = Math.Clamp(
                maxPositiveHeadwind /
                uav.MaxWindSpeedMps.Value,
                0m,
                1.5m);

            var headwindIncrease =
                0.20m * headwindRatio;

            multiplier += headwindIncrease;

            if (maxPositiveHeadwind > 0)
            {
                reasons.Add(
                    $"Headwind up to {maxPositiveHeadwind:0.##} m/s " +
                    $"increases the estimated battery requirement by " +
                    $"{headwindIncrease * 100m:0.##}%.");
            }

            if (weather.MaxGustMetersPerSecond.HasValue)
            {
                var gustRatio = Math.Clamp(
                    weather.MaxGustMetersPerSecond.Value /
                    uav.MaxWindSpeedMps.Value,
                    0m,
                    1.5m);

                var gustIncrease =
                    0.10m * gustRatio;

                multiplier += gustIncrease;

                reasons.Add(
                    $"Wind gusts up to " +
                    $"{weather.MaxGustMetersPerSecond.Value:0.##} m/s " +
                    $"increase the estimated battery requirement by " +
                    $"{gustIncrease * 100m:0.##}%.");
            }
        }
        else
        {
            reasons.Add(
                "Wind power adjustment is limited without a calibrated aircraft model; a mission wind limit is not a manufacturer capability or power model.");
        }

        if (weather.MaxRainMillimetersPerHour is > 0)
        {
            multiplier += 0.05m;

            reasons.Add(
                $"Forecast rain up to " +
                $"{weather.MaxRainMillimetersPerHour.Value:0.##} mm/hour " +
                "adds a 5% battery consumption adjustment.");
        }

        if (weather.ThunderstormForecast)
        {
            multiplier += 0.15m;

            reasons.Add(
                "A thunderstorm forecast adds a 15% battery consumption adjustment.");
        }

        return decimal.Round(
            multiplier,
            4,
            MidpointRounding.AwayFromZero);
    }

    private static double CalculateRouteDistanceMeters(
        IReadOnlyList<Waypoint> waypoints)
    {
        var totalDistanceMeters = 0d;

        for (var index = 1;
             index < waypoints.Count;
             index++)
        {
            var previous = waypoints[index - 1];
            var current = waypoints[index];

            totalDistanceMeters += HaversineDistanceMeters(
                previous.Latitude,
                previous.Longitude,
                current.Latitude,
                current.Longitude);
        }

        return totalDistanceMeters;
    }

    private static double HaversineDistanceMeters(
        decimal latitudeA,
        decimal longitudeA,
        decimal latitudeB,
        decimal longitudeB)
    {
        const double earthRadiusMeters = 6_371_000d;

        static double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180d;
        }

        var latitude1 = ToRadians((double)latitudeA);
        var latitude2 = ToRadians((double)latitudeB);

        var latitudeDifference = ToRadians(
            (double)(latitudeB - latitudeA));

        var longitudeDifference = ToRadians(
            (double)(longitudeB - longitudeA));

        var haversine =
            Math.Sin(latitudeDifference / 2d) *
            Math.Sin(latitudeDifference / 2d) +
            Math.Cos(latitude1) *
            Math.Cos(latitude2) *
            Math.Sin(longitudeDifference / 2d) *
            Math.Sin(longitudeDifference / 2d);

        var centralAngle =
            2d *
            Math.Atan2(
                Math.Sqrt(haversine),
                Math.Sqrt(1d - haversine));

        return earthRadiusMeters * centralAngle;
    }
}
