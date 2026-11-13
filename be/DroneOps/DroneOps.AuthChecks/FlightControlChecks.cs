using DroneOps.API;

internal static class FlightControlChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
        FlightControlState Ready(bool armed = false) => new() { Stage = "Ready", EvidenceAt = now, PilotAt = now,
            Evidence = new(Guid.NewGuid(), "PX4", armed, 1, true, true, "hash"), CompiledHash = "hash", UploadVerified = true };
        var state = Ready(true);
        state.Tick(now, true);
        Check(state.Stage == "Ready" && state.LaunchAt == null, "RC/GCS arming never starts a countdown");
        state = Ready();
        var command = state.Queue("Arm", now);
        state.Complete(command.Id, true, null, now);
        Check(state.Stage == "Arming" && state.LaunchAt == null, "undelivered ARM report cannot start countdown");
        command.DeliveredAt = now;
        state.Evidence = state.Evidence! with { Armed = true };
        state.Complete(command.Id, true, null, now);
        Check(state.Stage == "Countdown" && state.LaunchAt == now, "observed ARM makes the verified mission eligible for immediate scheduled START");
        state.EvidenceAt = now.AddSeconds(9); state.PilotAt = now.AddSeconds(9);
        state.Tick(now.AddSeconds(9), true);
        Check(state.Stage == "Countdown" && state.Pending == null, "countdown does not send start before deadline");
        state.Tick(now.AddSeconds(13), true);
        Check(state.Stage == "Cancelled" && state.LaunchAt == null, "stale FC/pilot cancels countdown");
        state.EvidenceAt = now.AddSeconds(14); state.PilotAt = now.AddSeconds(14);
        state.Tick(now.AddSeconds(14), true);
        Check(state.Stage == "Cancelled", "reconnection never resumes countdown");
        foreach (var cause in new[] { "consent", "pilot", "ground", "navigation", "disarmed" })
        {
            state = Ready(true); state.Stage = "Countdown"; state.LaunchAt = now.AddSeconds(10);
            if (cause == "pilot") state.PilotAt = now.AddSeconds(-4);
            if (cause == "ground") state.Evidence = state.Evidence! with { LandedState = null };
            if (cause == "navigation") state.Evidence = state.Evidence! with { NavigationReady = false };
            if (cause == "disarmed") state.Evidence = state.Evidence! with { Armed = false };
            state.Tick(now, cause != "consent");
            Check(state.Stage == "Cancelled" && state.Pending == null, "countdown fails closed on " + cause);
        }
        state = Ready(); command = state.Queue("Arm", now); command.DeliveredAt = now;
        state.Evidence = state.Evidence! with { Armed = true };
        state.Cancel("Pilot cancelled.");
        state.Complete(command.Id, true, null, now);
        Check(state.Stage == "Cancelled" && state.LaunchAt == null && command.Result == "Unknown", "late ACK cannot undo pilot cancellation");
        state = Ready(); command = state.Queue("Arm", now); command.DeliveredAt = now;
        state.EvidenceAt = now.AddSeconds(8); state.PilotAt = now.AddSeconds(8);
        state.Tick(now.AddSeconds(8), true);
        Check(state.Stage == "Unknown" && command.Result == "Unknown" && state.Pending == null, "timeout records unknown outcome without replay");
        state = Ready(); command = state.Queue("Upload", now); command.DeliveredAt = now;
        state.Complete(command.Id, true, "different-hash", now);
        Check(state.Stage == "Unknown", "read-back hash mismatch cannot mark upload ready");
        state = Ready(); state.UploadVerified = false; command = state.Queue("Upload", now); command.DeliveredAt = now;
        state.Complete(command.Id, true, "hash", now);
        Check(state.Stage == "Ready" && state.UploadVerified, "verified upload enables readiness without arming");
        state = Ready(true); command = state.Queue("Disarm", now); command.DeliveredAt = now;
        state.Complete(command.Id, true, null, now);
        Check(state.Stage == "Unknown", "DISARM acknowledgement alone cannot assert disarmed motors");
        state = Ready(false); command = state.Queue("Disarm", now); command.DeliveredAt = now;
        state.Complete(command.Id, true, null, now);
        Check(state.Stage == "Ready" && state.LaunchAt == null, "observed DISARM clears countdown");
        state = Ready(true); state.Stage = "Running"; state.Cancel("Too late.");
        Check(state.Stage == "Running", "cancellation never claims to undo an issued start");
        state = Ready(); state.EvidenceAt = now.AddSeconds(1);
        Check(!state.Fresh(now), "future evidence cannot satisfy freshness");
        state = Ready(); state.PilotAt = now.AddSeconds(1);
        Check(!state.PilotPresent(now), "future pilot timestamp cannot satisfy presence");
        Console.WriteLine($"Flight control checks passed: {passed}");
    }
}
