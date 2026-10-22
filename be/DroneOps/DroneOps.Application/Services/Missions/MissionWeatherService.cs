using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionWeatherOptions
{
    public decimal? WindWarningMetersPerSecond { get; set; }
    public decimal? GustWarningMetersPerSecond { get; set; }
    public decimal? RainWarningMillimetersPerHour { get; set; }
}
public interface IMissionWeatherService { Task<WeatherAdvisory> GetAsync(Mission mission,CancellationToken ct); }
public sealed class MissionWeatherService(HttpClient http,IOptions<MissionWeatherOptions> options,IMemoryCache cache) : IMissionWeatherService
{
    private sealed record CachedHours(JsonElement Hours,DateTimeOffset FetchedAt);
    private static readonly string[] Variables=["temperature_2m","relative_humidity_2m","wind_speed_10m","wind_direction_10m","wind_gusts_10m","precipitation_probability","rain","visibility","cloud_cover","weather_code"];
    public Task<WeatherAdvisory> GetAsync(Mission mission,CancellationToken ct)=>FetchAsync(mission,false,ct);
    public async Task<WeatherAdvisory> FetchAsync(Mission mission,bool force,CancellationToken ct)
    {
        var result=new WeatherAdvisory {MissionId=mission.Id,PlanHash=MissionValidationService.Hash(mission),FlightStartTime=mission.StartTime,FlightEndTime=mission.EndTime};
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(TimeSpan.FromSeconds(12));
        var points=mission.Waypoints.OrderBy(x=>x.SequenceOrder).ToList();
        var fetched=new Dictionary<string,(CachedHours? Data,bool Stale)>();
        foreach(var point in points)
        {
            // Spatial grouping (~1km at these latitudes). The response retains the exact waypoint.
            var lat=Math.Round(point.Latitude,2);var lon=Math.Round(point.Longitude,2);
            var key=$"open-meteo-v2:{lat.ToString("F2",CultureInfo.InvariantCulture)}:{lon.ToString("F2",CultureInfo.InvariantCulture)}";
            if(!fetched.TryGetValue(key,out var raw))
            {
                cache.TryGetValue<CachedHours>(key,out var stored);raw=(stored,false);
                if(force||stored is null||stored.FetchedAt<DateTimeOffset.UtcNow.AddMinutes(-10))
                {
                    try
                    {
                        var uri=$"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString("F2",CultureInfo.InvariantCulture)}&longitude={lon.ToString("F2",CultureInfo.InvariantCulture)}&hourly={string.Join(',',Variables)}&wind_speed_unit=ms&timezone=GMT&forecast_days=16";
                        using var doc=await http.GetFromJsonAsync<JsonDocument>(uri,budget.Token)??throw new JsonException();
                        var hours=doc.RootElement.GetProperty("hourly").Clone();
                        if(!hours.TryGetProperty("time",out var times)||times.ValueKind!=JsonValueKind.Array)throw new JsonException();
                        raw=(new CachedHours(hours,DateTimeOffset.UtcNow),false);cache.Set(key,raw.Data,TimeSpan.FromHours(24));
                    }
                    catch(Exception) when(!ct.IsCancellationRequested){raw=(stored,stored is not null);}
                }
                fetched[key]=raw;
            }
            var value=new WaypointWeather {SequenceOrder=point.SequenceOrder,Latitude=point.Latitude,Longitude=point.Longitude,AltitudeMeters=point.Altitude,EstimatedArrivalTime=null,ArrivalTimeReason="Cruise speed and waypoint dwell durations are not configured; flight-window forecast used instead of inventing an arrival time.",SourceFetchedAt=raw.Data?.FetchedAt};
            if(raw.Data is not null&&mission.StartTime.HasValue&&mission.EndTime.HasValue)
            {
                var hours=raw.Data.Hours;var times=hours.GetProperty("time");var first=mission.StartTime.Value;var last=mission.EndTime.Value;
                int selected=-1;var covered=new List<int>();
                for(int i=0;i<times.GetArrayLength();i++)
                {
                    if(!DateTime.TryParse(times[i].GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var at))continue;
                    if(at<last&&at.AddHours(1)>first){covered.Add(i);if(selected<0)selected=i;}
                }
                decimal? Metric(string name,int i)=>hours.TryGetProperty(name,out var values)&&values.ValueKind==JsonValueKind.Array&&i<values.GetArrayLength()&&values[i].ValueKind==JsonValueKind.Number&&values[i].TryGetDecimal(out var number)?number:null;
                if(selected>=0)
                {
                    value.ForecastTime=DateTime.Parse(times[selected].GetString()!,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);
                    value.TemperatureCelsius=Metric("temperature_2m",selected);value.RelativeHumidityPercent=Metric("relative_humidity_2m",selected);value.WindDirectionDegrees=Metric("wind_direction_10m",selected);value.PrecipitationProbabilityPercent=covered.Select(i=>Metric("precipitation_probability",i)).Max();value.VisibilityMeters=covered.Select(i=>Metric("visibility",i)).Min();value.CloudCoverPercent=Metric("cloud_cover",selected);
                    value.WindSpeedMetersPerSecond=covered.Select(i=>Metric("wind_speed_10m",i)).Max();value.WindGustMetersPerSecond=covered.Select(i=>Metric("wind_gusts_10m",i)).Max();value.PrecipitationMillimeters=covered.Select(i=>Metric("rain",i)).Max();
                    var thunder=covered.Select(i=>(int?)Metric("weather_code",i)).FirstOrDefault(x=>x is 95 or 96 or 99);value.WeatherCode=thunder??(int?)Metric("weather_code",selected);
                    var coveredTimes=covered.Select(i=>DateTime.Parse(times[i].GetString()!,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal)).Order().ToList();
                    bool complete=covered.All(i=>Variables.All(name=>Metric(name,i).HasValue))&&coveredTimes.Zip(coveredTimes.Skip(1)).All(pair=>pair.Second-pair.First<=TimeSpan.FromHours(1));
                    var end=DateTime.Parse(times[covered[^1]].GetString()!,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal).AddHours(1);
                    value.Status=raw.Stale?"Stale":complete&&value.ForecastTime<=first&&end>=last?"Available":"Partial";
                    if(value.Status!="Available")value.Warnings.Add($"WP{point.SequenceOrder}: forecast is {value.Status.ToLowerInvariant()} for the requested flight window.");
                    AddWarnings(value,options.Value);
                }
            }
            if(value.Status=="Unavailable")value.Warnings.Add($"WP{point.SequenceOrder}: forecast unavailable (provider error, missing data or outside forecast horizon).");
            value.RiskLevel=value.WeatherCode is 95 or 96 or 99?"High":value.Warnings.Count>0?"Review":"Low";
            result.Waypoints.Add(value);result.Warnings.AddRange(value.Warnings);
        }
        result.Status=points.Count==0||result.Waypoints.All(x=>x.Status=="Unavailable")?"Unavailable":result.Waypoints.Any(x=>x.Status=="Stale")?"Stale":result.Waypoints.All(x=>x.Status=="Available")?"Available":"Partial";
        result.Available=result.Status=="Available";result.OverallRisk=result.Status=="Unavailable"?"Unknown":result.Waypoints.Any(x=>x.RiskLevel=="High")?"High":result.Warnings.Count>0?"Review":"Low";
        result.FetchedAt=result.Waypoints.Where(x=>x.SourceFetchedAt.HasValue).Select(x=>x.SourceFetchedAt!.Value).DefaultIfEmpty(DateTimeOffset.UtcNow).Min();result.ExpiresAt=result.FetchedAt.AddMinutes(10);
        result.MaxWindMetersPerSecond=result.Waypoints.Select(x=>x.WindSpeedMetersPerSecond).Max();result.MaxGustMetersPerSecond=result.Waypoints.Select(x=>x.WindGustMetersPerSecond).Max();result.MaxRainMillimetersPerHour=result.Waypoints.Select(x=>x.PrecipitationMillimeters).Max();result.ThunderstormForecast=result.Waypoints.Any(x=>x.WeatherCode is 95 or 96 or 99);
        return result;
    }
    private static void AddWarnings(WaypointWeather w,MissionWeatherOptions p)
    {
        void Threshold(string name,decimal? value,decimal? threshold,string unit){if(value.HasValue&&threshold.HasValue&&value>=threshold)w.Warnings.Add($"WP{w.SequenceOrder}: {name} {value} {unit} >= configured advisory threshold {threshold} {unit}.");}
        Threshold("wind",w.WindSpeedMetersPerSecond,p.WindWarningMetersPerSecond,"m/s");Threshold("gust",w.WindGustMetersPerSecond,p.GustWarningMetersPerSecond,"m/s");
        if(w.PrecipitationMillimeters>0)Threshold("rain",w.PrecipitationMillimeters,p.RainWarningMillimetersPerHour??0,"mm/hour");
        if(w.WeatherCode is 95 or 96 or 99)w.Warnings.Add($"WP{w.SequenceOrder}: thunderstorm forecast (WMO {w.WeatherCode}).");
        if(!p.WindWarningMetersPerSecond.HasValue||!p.GustWarningMetersPerSecond.HasValue)w.Warnings.Add($"WP{w.SequenceOrder}: wind/gust advisory thresholds not configured; review reported values manually.");
    }
}
