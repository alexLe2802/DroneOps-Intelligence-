using System.Text.Json;
using DroneOps.Application.Services.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;

public static class RecoveryEnergyChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
        var now = DateTimeOffset.UtcNow;
        var uav = Guid.NewGuid();
        var profile = new RecoveryEnergyProfile("synthetic test fixture only", uav, "test-pack", "test-airframe", now.AddDays(-1), now.AddDays(1), 0.2m,
            5, 5, 40, 100, 200, 250, 150, 150, 150, 2, 20, 10, 2, 1.3m,
            [new(0, 0), new(20, 8), new(50, 40), new(100, 100)]);
        var mission = new Mission { Id = Guid.NewGuid(), UAVId = uav, AutomaticFlightRequested = true,
            AutomaticFlightLimits = new(RecoveryEnergyProfile: profile), AltitudeReference = "Relative", Status = "Approved",
            AverageSpeedMetersPerSecond = 2, MaxSpeedMetersPerSecond = 3, StartTime = now.AddMinutes(-5).UtcDateTime,
            EndTime = now.AddHours(1).UtcDateTime,
            Waypoints = [new() { SequenceOrder = 1, Latitude = 10, Longitude = 106, Altitude = 20, ActionType = "TakeOff" },
                new() { SequenceOrder = 2, Latitude = 10.001m, Longitude = 106, Altitude = 20, ActionType = "Land",
                    Navigation = new(LandingGroundAltitudeMeters: 0, LandingSiteSurvey: new(now.AddHours(-1), now.AddHours(2), true, true, "synthetic site survey")) }] };
        var sample = new TelemetryRecord { UAVId = uav, MissionId = mission.Id, SourceTimestamp = now, ReceivedAt = now,
            IsValid = true, Latitude = 10, Longitude = 106, Altitude = 10, AltitudeReference = "Relative", BatteryLevel = 80, BatteryTemperatureCelsius = 25 };
        var home = new RecoveryPosition(10, 106, 0);
        RecoveryEnergyAssessment Assess(TelemetryRecord? t = null) => RecoveryEnergyCalculator.Assess(mission, t ?? sample, home, now);
        var result = Assess();
        Check(result.Status == "PlanningEstimate" && result.AvailableEnergyWh == 40 && result.ReserveEnergyWh == 20, "SOC uses the lower measured checkpoint, with reserve from usable capacity");
        Check(result.Sites.Single().RequiredEnergyWh > 0 && result.Sites.Single().FitsDeclaredEnergyBudget == true, "cost includes horizontal/vertical travel, landing, hold, margin and reserve");
        Check(!result.CanAuthorizeRecovery, "even complete declarations cannot authorize aircraft recovery");
        sample.BatteryLevel = 49;
        Check(Assess().AvailableEnergyWh == 8 && Assess().Sites.Single().FitsDeclaredEnergyBudget == false, "no upward linear interpolation between SOC checkpoints");
        sample.BatteryLevel = 80;
        mission.AverageSpeedMetersPerSecond = 1;
        Check(Assess().AvailableEnergyWh == null, "slower approved cruise cannot extrapolate a faster measured power profile");
        mission.AverageSpeedMetersPerSecond = 2;
        sample.BatteryLevel = 15;
        Check(Assess().MissingData.Any(reason => reason.Contains("Critical battery", StringComparison.Ordinal)) && !Assess().CanAuthorizeRecovery,
            "critical battery cannot become authorization for a new return leg");
        sample.BatteryLevel = 80;
        sample.SourceTimestamp = now.AddSeconds(-4);
        var stale = Assess();
        Check(stale.AvailableEnergyWh == null && stale.Sites.Single().RequiredEnergyWh == null && stale.Sites.Single().DistanceMeters == null, "stale telemetry fabricates neither energy nor a zero distance");
        sample.SourceTimestamp = now;
        sample.ReceivedAt = now.AddSeconds(-4);
        Check(Assess().AvailableEnergyWh == null, "fresh source timestamps cannot refresh stale receiver evidence");
        sample.ReceivedAt = now;
        sample.MissionId = Guid.NewGuid();
        Check(Assess().AvailableEnergyWh == null, "another mission's telemetry cannot supply available energy");
        sample.MissionId = mission.Id;
        sample.AltitudeReference = "MSL";
        Check(Assess().AvailableEnergyWh == null, "MSL telemetry cannot silently become a relative recovery altitude");
        sample.AltitudeReference = "Relative";
        sample.BatteryTemperatureCelsius = null;
        Check(Assess().AvailableEnergyWh == null, "missing temperature cannot establish measured-envelope validity");
        sample.BatteryTemperatureCelsius = 45;
        Check(Assess().AvailableEnergyWh == null, "out-of-envelope battery temperature blocks energy estimate");
        sample.BatteryTemperatureCelsius = 25;
        Check(RecoveryEnergyCalculator.Assess(mission, sample, null, now).AvailableEnergyWh == null, "unknown actual Home blocks live recovery estimates");
        var site = mission.Waypoints.Last();
        site.Navigation = site.Navigation! with { LandingSiteSurvey = null };
        Check(!Assess().Sites.Single().SurveyDeclaredCurrent && !Assess().CanAuthorizeRecovery, "ordinary coordinates do not establish site clearance");
        site.Navigation = site.Navigation with { LandingSiteSurvey = new(now.AddHours(-2), now.AddHours(-1), true, true, "expired test report") };
        Check(!Assess().Sites.Single().SurveyDeclaredCurrent, "expired survey is not a current declaration");
        site.Navigation = site.Navigation with { LandingGroundAltitudeMeters = null };
        Check(Assess().Sites.Single().RequiredEnergyWh == null, "missing landing ground does not become cruising height or Home ground");
        site.Navigation = site.Navigation with { LandingGroundAltitudeMeters = 25 };
        Check(Assess().Sites.Single().RequiredEnergyWh == null, "landing geometry above return altitude is not estimated as descent");
        site.Navigation = site.Navigation with { LandingGroundAltitudeMeters = 0 };
        foreach (var invalid in new[] {
            profile with { UavId = Guid.NewGuid() }, profile with { BatteryIdentifier = "" }, profile with { CruisePowerWatts = 0 }, profile with { EnergyMarginFactor = 1 },
            profile with { ValidUntilUtc = now.AddMinutes(-1) }, profile with { MeasuredAtUtc = now.AddHours(1) },
            profile with { MaxWindMetersPerSecond = 2 }, profile with { RemainingEnergyCurve = [new(0,0),new(50,70),new(100,60)] },
            profile with { RemainingEnergyCurve = [new(0,0),new(50,40),new(50,60),new(100,100)] },
            profile with { RemainingEnergyCurve = [new(0,0),null!,new(100,100)] },
            profile with { RemainingEnergyCurve = null! }, profile with { ReturnAltitudeMeters = 31 },
        })
        {
            mission.AutomaticFlightLimits = new(RecoveryEnergyProfile: invalid);
            Check(Assess().AvailableEnergyWh == null, "malformed/expired/out-of-envelope profile fails closed");
        }
        mission.AutomaticFlightLimits = new(RecoveryEnergyProfile: profile);
        var hash = MissionValidationService.Hash(mission);
        mission.AutomaticFlightLimits = new(RecoveryEnergyProfile: profile with { CruisePowerWatts = 300 });
        Check(hash != MissionValidationService.Hash(mission), "measurement changes invalidate the approved automatic plan hash");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Check(!JsonSerializer.Serialize(new AutomaticFlightLimits(), json).Contains("recoveryEnergyProfile"), "absent optional profile preserves legacy envelope serialization");
        Check(!JsonSerializer.Serialize(new WaypointNavigation(), json).Contains("landingSiteSurvey"), "absent optional survey preserves legacy waypoint serialization");
        Console.WriteLine($"Recovery energy checks passed: {count}");
    }
}
