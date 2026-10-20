using System.Security.Claims;
using DroneOps.Application.Services.Missions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace DroneOps.API.Controllers.Missions;

[ApiController,Route("api/missions"),Authorize(Roles="uav_operator,operation_manager")]
public sealed class MissionEvidenceController(MissionValidationService validation,MissionWeatherQueryService weather) : ControllerBase
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw new System.Security.Authentication.AuthenticationException("Invalid user identity.");
    private bool Manager=>User.IsInRole("operation_manager");
    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id,CancellationToken ct)=>Ok(await validation.HistoryAsync(id,Actor,Manager,ct));
    [HttpGet("{id:guid}/validation")]
    public async Task<IActionResult> Validation(Guid id,[FromQuery] Guid? updateRequestId,CancellationToken ct)=>Ok(await validation.ReadAsync(id,Actor,Manager,updateRequestId,ct));
    [HttpGet("{id:guid}/weather")]
    public async Task<IActionResult> Weather(Guid id,[FromQuery] Guid? updateRequestId,CancellationToken ct)=>Ok(await weather.ReadAsync(id,Actor,Manager,updateRequestId,false,ct));
    [HttpPost("{id:guid}/weather/refresh")]
    public async Task<IActionResult> RefreshWeather(Guid id,[FromQuery] Guid? updateRequestId,CancellationToken ct)=>Ok(await weather.ReadAsync(id,Actor,Manager,updateRequestId,true,ct));
}
