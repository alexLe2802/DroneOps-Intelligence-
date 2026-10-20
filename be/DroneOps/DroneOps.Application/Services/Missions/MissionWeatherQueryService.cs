using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace DroneOps.Application.Services.Missions;

public sealed class MissionWeatherQueryService(AppDbContext db,IMissionWeatherService weather,IOptions<MissionWeatherOptions> policy)
{
    public async Task<WeatherAdvisory> ReadAsync(Guid id,Guid actor,bool manager,Guid? requestId,bool refresh,CancellationToken ct)
    {
        var m=await db.Missions.AsNoTracking().Include(x=>x.Waypoints).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new KeyNotFoundException("Mission not found.");
        if(!manager&&m.CreatedBy!=actor)throw new UnauthorizedAccessException("Mission is outside your scope.");
        if(requestId.HasValue){var request=await db.MissionUpdateRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==requestId&&x.MissionId==id,ct)??throw new KeyNotFoundException("Update request not found.");m=MissionPlans.Propose(m,request);}
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MissionValidationService.Hash(m)+requestId+JsonSerializer.Serialize(policy.Value))));
        var cached=await db.MissionWeatherCaches.AsNoTracking().SingleOrDefaultAsync(x=>x.Key==key,ct);
        if(!refresh&&cached is not null)
        {
            var result=JsonSerializer.Deserialize<WeatherAdvisory>(cached.ForecastJson,MissionValidationService.JsonOptions)!;
            if(cached.ExpiresAt<DateTimeOffset.UtcNow){result.Status="Stale";result.Available=false;result.Warnings.Add("Cached forecast expired. Refresh to obtain a new forecast.");}return result;
        }
        var value=weather is MissionWeatherService provider?await provider.FetchAsync(m,refresh,ct):await weather.GetAsync(m,ct);value.UpdateRequestId=requestId;value.MissionId=id;value.PlanHash=MissionValidationService.Hash(m);value.FlightStartTime=m.StartTime;value.FlightEndTime=m.EndTime;
        // Forecast failure does not destroy an earlier forecast; return it explicitly stale.
        if(value.Status=="Unavailable"&&cached is not null){var old=JsonSerializer.Deserialize<WeatherAdvisory>(cached.ForecastJson,MissionValidationService.JsonOptions)!;old.Status="Stale";old.Available=false;old.Warnings.Add("Provider refresh failed; previous forecast is shown as stale.");return old;}
        if(value.Status=="Unavailable")value.ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(1);
        var json=JsonSerializer.Serialize(value,MissionValidationService.JsonOptions);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"MissionWeatherCache\" (\"Key\",\"ForecastJson\",\"FetchedAt\",\"ExpiresAt\") VALUES ({key},{json}::jsonb,{value.FetchedAt},{(value.Status=="Unavailable"?DateTimeOffset.UtcNow.AddMinutes(1):value.ExpiresAt)}) ON CONFLICT (\"Key\") DO UPDATE SET \"ForecastJson\"=EXCLUDED.\"ForecastJson\",\"FetchedAt\"=EXCLUDED.\"FetchedAt\",\"ExpiresAt\"=EXCLUDED.\"ExpiresAt\"",ct);
        return value;
    }
}
