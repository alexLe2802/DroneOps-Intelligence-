using System.Net;
using System.Text.Json;
using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

internal static class MissionWeatherChecks
{
    public static async Task RunAsync()
    {
        int passed=0;
        void Check(bool ok,string message){if(!ok)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);passed++;}
        var hour=new DateTime(DateTime.UtcNow.Year,DateTime.UtcNow.Month,DateTime.UtcNow.Day,DateTime.UtcNow.Hour,0,0,DateTimeKind.Utc);
        var payload=JsonSerializer.Serialize(new {hourly=new {time=new[]{hour.ToString("yyyy-MM-ddTHH:mm"),hour.AddHours(1).ToString("yyyy-MM-ddTHH:mm")},temperature_2m=new[]{28,29},relative_humidity_2m=new[]{70,75},wind_speed_10m=new[]{12,12},wind_direction_10m=new[]{180,200},wind_gusts_10m=new[]{18,18},rain=new[]{3,3},precipitation_probability=new[]{80,90},visibility=new[]{9000,8000},cloud_cover=new[]{60,70},weather_code=new[]{95,95}}});
        var handler=new FixtureHandler(payload);using var http=new HttpClient(handler);using var cache=new MemoryCache(new MemoryCacheOptions());
        var service=new MissionWeatherService(http,Options.Create(new MissionWeatherOptions {WindWarningMetersPerSecond=10,GustWarningMetersPerSecond=15,RainWarningMillimetersPerHour=2}),cache);
        var mission=new Mission {Id=Guid.NewGuid(),StartTime=hour.AddMinutes(1),EndTime=hour.AddMinutes(59),Waypoints=[new(){SequenceOrder=1,Latitude=10,Longitude=106},new(){SequenceOrder=2,Latitude=10.001m,Longitude=106.001m}]};
        var weather=await service.GetAsync(mission,default);var first=weather.Waypoints[0];
        Check(weather.Status=="Available"&&weather.MaxWindMetersPerSecond==12,"full forecast is available with explicit m/s units");
        Check(first.TemperatureCelsius==28&&first.RelativeHumidityPercent==70&&first.WindDirectionDegrees==180,"temperature, humidity and wind direction retain real source values");
        Check(first.PrecipitationProbabilityPercent==80&&first.VisibilityMeters==9000&&first.CloudCoverPercent==60,"rain probability, visibility and cloud cover are parsed");
        Check(weather.Waypoints.Count==2&&handler.Count==1,"nearby waypoints are grouped without losing individual evidence");
        Check(first.Warnings.Count==4&&weather.ThunderstormForecast,"threshold warnings identify waypoint and weather condition");
        Check(first.EstimatedArrivalTime==null&&!string.IsNullOrWhiteSpace(first.ArrivalTimeReason),"arrival time remains unknown with an explicit reason when flight speeds are unavailable");
        Check(handler.LastUri!.Query.Contains("wind_speed_unit=ms"),"provider unit is explicitly requested");
        await service.GetAsync(mission,default);Check(handler.Count==1,"fresh spatial forecast cache avoids repeated provider calls");
        await service.FetchAsync(mission,true,default);Check(handler.Count==2,"explicit refresh bypasses fresh cache");
        mission.StartTime=hour.AddDays(30);mission.EndTime=mission.StartTime.Value.AddHours(1);
        Check((await service.GetAsync(mission,default)).Status=="Unavailable","outside forecast horizon stays unavailable, never synthetic weather");
        mission.StartTime=hour.AddMinutes(1);mission.EndTime=hour.AddMinutes(59);
        handler.Payload="{\"hourly\":{\"time\":[\""+hour.ToString("yyyy-MM-ddTHH:mm")+"\"],\"rain\":[null]}}";
        var partial=await service.FetchAsync(mission,true,default);
        Check(partial.Status=="Partial"&&partial.Waypoints[0].TemperatureCelsius==null,"missing sensor/forecast fields remain null with Partial status");
        handler.Fail=true;
        var stale=await service.FetchAsync(mission,true,default);
        Check(stale.Status=="Stale","provider interruption falls back to explicit stale forecast");
        mission.Waypoints=[new(){SequenceOrder=1,Latitude=-20,Longitude=-100}];
        Check((await service.FetchAsync(mission,true,default)).Status=="Unavailable","provider interruption without cache is unavailable");
        handler.Fail=false;handler.Payload="{}";
        Check((await service.FetchAsync(mission,true,default)).Status=="Unavailable","malformed provider data does not block or fabricate weather");
        handler.Code=HttpStatusCode.TooManyRequests;
        Check((await service.FetchAsync(mission,true,default)).Status=="Unavailable","provider rate limit is explicit unavailable rather than fake weather");
        handler.Code=null;handler.Timeout=true;
        Check((await service.FetchAsync(mission,true,default)).Status=="Unavailable","provider timeout is non-blocking unavailable weather");
        Console.WriteLine($"{passed} weather contract checks passed with HTTP fixtures.");
    }
    private sealed class FixtureHandler(string payload):HttpMessageHandler
    {
        public string Payload=payload;public bool Fail;public bool Timeout;public HttpStatusCode? Code;public int Count;public Uri? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Count++;LastUri=request.RequestUri;if(Timeout)throw new TaskCanceledException("Simulated provider timeout");if(Code.HasValue)return Task.FromResult(new HttpResponseMessage(Code.Value));if(Fail)throw new HttpRequestException("Simulated provider outage");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Payload,System.Text.Encoding.UTF8,"application/json")});
        }
    }
}
