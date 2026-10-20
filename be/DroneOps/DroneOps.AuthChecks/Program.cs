using System.Reflection;
using DroneOps.API.Controllers;
using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.Validators.Users;
using DroneOps.Domain;
using Microsoft.AspNetCore.Authorization;

if (args.Length == 2 && args[0] == "--license-workflow")
{
    await LicenseWorkflowChecks.RunAsync(Path.GetFullPath(args[1]));
    return;
}

if (args.Length == 2 && args[0] == "--mission-approval")
{
    await MissionApprovalChecks.RunAsync(Path.GetFullPath(args[1]));
    return;
}

if (args.Length == 1 && args[0] == "--mission-weather")
{
    await MissionWeatherChecks.RunAsync();
    return;
}

// Offline checks for the current JWT API. No database or external identity provider is used.
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
foreach (var role in new[] { "Admin", "SystemAdmin", "OperationsManager", "operations_manager", "operation_manager" })
    Check(CanonicalRoles.Normalize(role) == "operation_manager", "Manager normalization: " + role);
Check(CanonicalRoles.Normalize("Pilot") == "uav_operator", "Legacy pilot normalization");
foreach (var action in new[] { "GetAll", "GetDetail", "Approve", "Reject" })
{
    var method = typeof(PilotRegistrationController).GetMethod(action)!;
    Check(method.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "operation_manager"
        && method.GetCustomAttribute<AllowAnonymousAttribute>() is null,
        "Registration review requires manager: " + action);
}
Check(typeof(AvatarController).GetCustomAttribute<AuthorizeAttribute>() is not null,
    "Avatar controller requires authentication");
Check(!typeof(AvatarController).GetMethods().Any(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null),
    "No anonymous avatar actions");
Check(typeof(LicenseChangesController).GetCustomAttribute<AuthorizeAttribute>() is not null,
    "License history and evidence require authentication");
Check(typeof(LicenseChangesController).GetMethod("Submit")!.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "uav_operator",
    "Only operators can submit license changes");
Check(typeof(LicenseChangesController).GetMethod("Review")!.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "operation_manager",
    "Only managers can review license changes");
Check(typeof(OperatorCredentialsController).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "operation_manager",
    "Other operators' credentials and avatars require manager authorization");
var validator = new CreatePilotRegistrationValidator();
var today = DateOnly.FromDateTime(DateTime.UtcNow);
CreatePilotRegistrationRequest Valid() => new()
{
    FullName = "Test Pilot", Email = "pilot@example.com", Password = "Test-password-123!",
    DroneType = "Multirotor", PilotLicenseNo = "LICENSE-123", UsagePurpose = "Survey",
    DateOfBirth = today.AddYears(-25), LicenseIssuedDate = today.AddDays(-30),
    LicenseExpiredDate = today.AddYears(1), ExperienceYears = 1
};
Check(validator.Validate(Valid()).IsValid, "Valid adult pilot application accepted");
var request = Valid(); request.DateOfBirth = today.AddYears(-17);
Check(!validator.Validate(request).IsValid, "Underage applicant rejected");
request = Valid(); request.LicenseExpiredDate = today.AddDays(-1);
Check(!validator.Validate(request).IsValid, "Expired license rejected");
request = Valid(); request.LicenseIssuedDate = today.AddYears(2);
Check(!validator.Validate(request).IsValid, "Reversed license dates rejected");
request = Valid(); request.Password = "short";
Check(!validator.Validate(request).IsValid, "Short password rejected");
Console.WriteLine($"{passed} current auth contract checks passed. Legacy Firebase session checks are archived and were not executed.");
