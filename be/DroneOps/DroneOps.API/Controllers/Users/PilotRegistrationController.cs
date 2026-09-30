using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Route("api/pilot-registrations")]
public class PilotRegistrationController
    : ControllerBase
{
    private readonly IPilotRegistrationService _service;

    public PilotRegistrationController(
        IPilotRegistrationService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Register(
     CreatePilotRegistrationRequest request)
    {
        try
        {
            await _service.RegisterAsync(request);

            return Ok(new
            {
                Message = "Registration submitted successfully."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                Message = ex.Message
            });
        }
    }

    [Authorize(Roles = "Admin,SystemAdmin")]
    [HttpGet]
    public async Task<IActionResult> GetAll(
    [FromQuery] GetPilotRegistrationsRequest request)
    {
        var result = await _service.GetAllAsync(request);

        return Ok(result);
    }

    [Authorize(Roles = "Admin,SystemAdmin")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id)
    {
        var result = await _service.GetDetailAsync(id);

        if (result == null)
        {
            return NotFound(new
            {
                Message = "Pilot registration not found."
            });
        }

        return Ok(result);
    }
}