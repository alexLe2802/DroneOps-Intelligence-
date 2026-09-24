using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DroneOps.Persistence.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Auth;

[ApiController, Route("api/accounts"), Authorize(Roles = AccountRoles.Manager)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountsController(IAuthStore store) : ControllerBase
{
    private Guid ManagerId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await store.ListAccountsAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Provision(ProvisionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) return BadRequest(new { message = "Display name is required." });
        // The client cannot choose a role; this endpoint provisions Operators only (SRS UC-03).
        return await store.ProvisionOperatorAsync(request.Email, request.DisplayName, ManagerId, ct)
            ? StatusCode(201, new { message = "Operator access granted." })
            : Conflict(new { message = "This email already has an account." });
    }

    [HttpPatch("{id:guid}/access")]
    public async Task<IActionResult> Access(Guid id, AccessRequest request, CancellationToken ct) =>
        await store.SetOperatorAccessAsync(id, request.IsActive, ManagerId, ct)
            ? NoContent() : NotFound(new { message = "Operator account not found. Manager access cannot be changed here." });
}
public sealed record ProvisionRequest([Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(120, MinimumLength = 1)] string DisplayName);
public sealed record AccessRequest(bool IsActive);
