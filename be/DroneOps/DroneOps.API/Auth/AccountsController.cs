using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DroneOps.Persistence.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace DroneOps.API.Auth;

[ApiController, Route("api/accounts"), Authorize(Roles = AccountRoles.Manager)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountsController(IAuthStore store, IFirebaseAccountDirectory directory) : ControllerBase
{
    private Guid ManagerId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await store.ListAccountsAsync(ct));

    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Provision(ProvisionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) return BadRequest(new { message = "Display name is required." });
        var email = request.Email.Trim().ToLowerInvariant();
        var name = request.DisplayName.Trim();
        if (await store.AccountExistsAsync(email, ct))
            return Conflict(new { code = "account_exists", message = "This email already has a DroneOps account. Manage its access in the account list." });
        try
        {
            var identity = request.AccountType == "new"
                ? await directory.CreateAsync(email, name, request.Password!, ct)
                : await directory.FindByEmailAsync(email, ct);
            if (identity.Disabled)
                return Conflict(new { code = "firebase_account_disabled", message = "This account is disabled in Firebase. Restore it there before granting Pilot access." });
            // Role is fixed on the server. Bind the UID now, not to whoever first uses this email.
            // The unique constraints also protect against simultaneous Manager requests.
            var created = await store.ProvisionOperatorAsync(email, name, identity.Uid, request.IsActive, ManagerId, ct);
            return created
                ? StatusCode(201, new { message = request.AccountType == "new"
                    ? "Pilot account created. Share the initial password securely. The Pilot must verify their email at first sign-in."
                    : "Pilot access granted to the existing Firebase account. Its sign-in credentials are unchanged." })
                : Conflict(new { code = "account_exists", message = "This email or Firebase identity already has a DroneOps account. Refresh the account list." });
        }
        catch (AccountProvisioningException e)
        {
            return StatusCode(e.Code == "invalid_account" ? 400 : 409, new { code = e.Code, message = e.Message });
        }
        catch (Exception e) when (e is IdentityUnavailableException or NpgsqlException or TimeoutException)
        {
            // These services have no distributed transaction. A timeout can happen after a commit.
            // Never delete or reset a Firebase identity on an ambiguous failure. It cannot enter
            // DroneOps without an active DB account. Managers can recover via the existing mode.
            return StatusCode(503, new { code = "provisioning_unavailable", message = "Could not confirm account creation. Refresh the account list first. If it is missing but exists in Firebase, choose an existing Firebase account to finish granting access." });
        }
    }

    [HttpPatch("{id:guid}/access")]
    public async Task<IActionResult> Access(Guid id, AccessRequest request, CancellationToken ct) =>
        await store.SetOperatorAccessAsync(id, request.IsActive, ManagerId, ct)
            ? NoContent() : NotFound(new { message = "Operator account not found. Manager access cannot be changed here." });
}
public sealed record ProvisionRequest([Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(120, MinimumLength = 1)] string DisplayName,
    [Required, RegularExpression("^(new|existing)$")] string AccountType,
    [StringLength(128)] string? Password = null,
    bool IsActive = true) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AccountType == "new" && (Password is null || Password.Length < 12 || string.IsNullOrWhiteSpace(Password)))
            yield return new("Use an initial password of 12–128 characters.", [nameof(Password)]);
        if (AccountType == "existing" && Password is not null)
            yield return new("Do not supply a password for an existing Firebase account.", [nameof(Password)]);
    }
}
public sealed record AccessRequest(bool IsActive);
