using DroneOps.Application.Interfaces.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Authorize(Roles = "operation_manager")]
[Route("api/users/{userId:guid}")]
public sealed class OperatorCredentialsController : ControllerBase
{
    private readonly IOperatorCredentialsService _service;

    public OperatorCredentialsController(
        IOperatorCredentialsService service)
    {
        _service = service;
    }

    [HttpGet("avatar")]
    public async Task<IActionResult> Avatar(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var avatarUrl = await _service.GetAvatarAsync(
            userId,
            cancellationToken);

        Response.Headers.CacheControl = "no-store";

        return avatarUrl is null
            ? NotFound()
            : Redirect(avatarUrl);
    }

    [HttpGet("credentials")]
    public async Task<IActionResult> Credentials(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetCredentialsAsync(
            userId,
            cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }
}