using DroneOps.Application.Services.AI;
using DroneOps.Application.Validators.Missions;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Domain.Entities;

internal static class BatteryAssessmentChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var uav = new UAV { Id = Guid.NewGuid(), BatteryCapacityMah = 5200, MaxFlightTimeMinutes = 30 };
        var mission = new Mission { Id = Guid.NewGuid(), UAVId = uav.Id, AverageSpeedMetersPerSecond = 5,
            Waypoints = [new Waypoint { SequenceOrder = 1, Latitude = 10, Longitude = 106 },
                new Waypoint { SequenceOrder = 2, Latitude = 10.001m, Longitude = 106 }] };
        TelemetryRecord Sample() => new() { UAVId = uav.Id, MissionId = mission.Id, IsValid = true,
            SourceTimestamp = now, ReceivedAt = now, BatteryLevel = 70, BatteryConsumedMah = 300 };
        var sample = Sample();
        var result = BatteryAssessmentCalculator.Calculate(mission, uav, sample, null, now);
        Check(result.Status == "EstimateOnly" && result.CurrentBatteryPercent == 70 && result.ConsumedMah == 300, "battery evidence preserves percent and consumed charge as separate FC measurements");
        Check(result.EstimatedRemainingChargeMah == 3640 && result.ConfiguredCapacityMah == 5200, "remaining charge is explicitly estimated from configured capacity, never inferred from consumed mAh");
        Check(result.RequiredBatteryPercent > 20 && result.EstimatedRequiredChargeMah == decimal.Round(5200m * result.RequiredBatteryPercent!.Value / 100m, 1), "mission charge requirement reuses the existing route/endurance calculator including reserve");
        Check(result.MeetsEstimatedRequirement == true && result.CapacitySource.Contains("not reported"), "an estimate identifies the configured source and cannot claim measured full capacity");
        sample.BatteryVoltageVolts = 0.003m;
        result = BatteryAssessmentCalculator.Calculate(mission, uav, sample, null, now);
        Check(result.CurrentBatteryPercent == 70 && result.RequiredBatteryPercent == null && result.MeetsEstimatedRequirement == null
            && result.MissingData.Any(text => text.Contains("below 1 V")), "USB-powered FC reporting percentage with millivolt pack voltage cannot establish flight energy");
        sample.BatteryVoltageVolts = null;
        sample.BatteryLevel = 0; sample.BatteryConsumedMah = 0;
        result = BatteryAssessmentCalculator.Calculate(mission, uav, sample, null, now);
        Check(result.CurrentBatteryPercent == 0 && result.ConsumedMah == 0 && result.MeetsEstimatedRequirement == false, "real zero battery and zero consumed charge remain valid values");
        sample.BatteryLevel = null; sample.BatteryVoltageVolts = 25;
        result = BatteryAssessmentCalculator.Calculate(mission, uav, sample, null, now);
        Check(result.Status == "MissingData" && result.RequiredBatteryPercent == null, "missing battery percentage is never guessed from voltage");
        foreach (var missing in new TelemetryRecord?[] { null, new() { UAVId = uav.Id, MissionId = mission.Id, IsValid = true, SourceTimestamp = now.AddSeconds(-16), ReceivedAt = now, BatteryLevel = 80 },
            new() { UAVId = Guid.NewGuid(), MissionId = mission.Id, IsValid = true, SourceTimestamp = now, ReceivedAt = now, BatteryLevel = 80 },
            new() { UAVId = uav.Id, MissionId = Guid.NewGuid(), IsValid = true, SourceTimestamp = now, ReceivedAt = now, BatteryLevel = 80 },
            new() { UAVId = uav.Id, MissionId = mission.Id, IsValid = false, SourceTimestamp = now, ReceivedAt = now, BatteryLevel = 80 },
            new() { UAVId = uav.Id, MissionId = mission.Id, IsValid = true, SourceTimestamp = now.AddSeconds(6), ReceivedAt = now, BatteryLevel = 80 } })
        {
            result = BatteryAssessmentCalculator.Calculate(mission, uav, missing, null, now);
            Check(result.CurrentBatteryPercent == null && result.RequiredBatteryPercent == null && result.MeetsEstimatedRequirement == null, "missing/stale/foreign/invalid/future telemetry cannot establish battery sufficiency");
        }
        uav.BatteryCapacityMah = null;
        result = BatteryAssessmentCalculator.Calculate(mission, uav, Sample(), null, now);
        Check(result.Status == "MissingData" && result.EstimatedRequiredChargeMah == null, "unknown capacity prevents numeric requirement and remaining-mAh claims");
        uav.BatteryCapacityMah = 5200; uav.MaxFlightTimeMinutes = null;
        Check(BatteryAssessmentCalculator.Calculate(mission, uav, Sample(), null, now).RequiredBatteryPercent == null, "missing endurance prevents a mission consumption estimate");
        uav.MaxFlightTimeMinutes = 1;
        mission.Waypoints.Last().Latitude = 12;
        result = BatteryAssessmentCalculator.Calculate(mission, uav, Sample(), null, now);
        Check(result.RequiredBatteryPercent > 100 && result.MeetsEstimatedRequirement == false, "impossible battery demand is not clamped to a plausible value");
        var request = new CreateMissionRequest { Waypoints = [new() { SequenceOrder = 1, ActionType = "TakeOff" }, new() { SequenceOrder = 2, ActionType = "CapturePhoto" }, new() { SequenceOrder = 3, ActionType = "Land" }] };
        var rejected = new CreateMissionRequestValidator().Validate(request);
        Check(rejected.Errors.Any(e => e.PropertyName.Contains("ActionType") && e.ErrorMessage.Contains("deferred")), "new camera mission actions are rejected while the feature is deferred");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
