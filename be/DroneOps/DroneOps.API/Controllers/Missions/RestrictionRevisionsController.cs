using DroneOps.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace DroneOps.API.Controllers.Missions;

[ApiController,Route("api/geofences"),Authorize(Roles="uav_operator,operation_manager")]
public sealed class RestrictionRevisionsController(AppDbContext db) : ControllerBase
{
    [HttpGet("{id:guid}/revisions")]
    public async Task<IActionResult> Get(Guid id,CancellationToken ct)
    {
        var rows=await db.GeofenceRevisions.AsNoTracking().Where(x=>x.GeofenceId==id).OrderByDescending(x=>x.Revision).ToListAsync(ct);
        return rows.Count==0?NotFound():Ok(rows.Select(x=>new{x.Id,x.GeofenceId,x.Revision,x.Action,x.RecordedAt,snapshot=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(x.SnapshotJson)}));
    }
}
