using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.DTOs.Response.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

internal static class DroneGatewayHttpChecks
{
    public static async Task RunAsync(string root,Guid owner,Guid uav,Guid mission)
    {
        var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(root,"be/DroneOps/DroneOps.API/appsettings.json"))
            .AddJsonFile(Path.Combine(root,"be/DroneOps/DroneOps.API/appsettings.Local.json")).AddEnvironmentVariables().Build();
        var jwt=new JwtSecurityToken(config["Jwt:Issuer"],config["Jwt:Audience"],
            [new Claim(ClaimTypes.NameIdentifier,owner.ToString()),new Claim(ClaimTypes.Role,"uav_operator")],
            expires:DateTime.UtcNow.AddMinutes(5),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)),SecurityAlgorithms.HmacSha256));
        using var client=new HttpClient{BaseAddress=new Uri("http://localhost:5159"),Timeout=TimeSpan.FromSeconds(20)};
        var unauthorized=await client.GetAsync("/api/drone-connections");
        if(unauthorized.StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Session list must require pilot authentication.");
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new JwtSecurityTokenHandler().WriteToken(jwt));
        var creation=await client.PostAsJsonAsync("/api/drone-connections",new CreateDroneConnection(uav,mission));
        creation.EnsureSuccessStatusCode();
        var pair=(await creation.Content.ReadFromJsonAsync<DronePairing>())!;
        if(pair.GatewayPath!="/ws/drone-gateway"||pair.Connection.GatewayTransport!="WebSocket")throw new Exception("Pairing response does not advertise the WebSocket gateway contract.");
        Console.WriteLine("PASS: real HTTP controller creates a pilot-scoped pairing session");
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DRONEOPS_GATEWAY_PYTHON")??"python3")
        {RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        start.ArgumentList.Add(Path.Combine(root,"gateway/http_smoke.py"));start.ArgumentList.Add("http://localhost:5159");
        using var process=Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(pair.PairingCode);process.StandardInput.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}
        if(process.ExitCode!=0)throw new Exception("Gateway WebSocket smoke failed: "+await errors);
        Console.WriteLine((await output).Trim());
        var latest=await client.GetFromJsonAsync<MissionLatestTelemetryResponse>($"/api/missions/{mission}/telemetry/latest");
        if(latest?.Latitude!=11.35m||latest.Longitude!=106.34m||latest.GroundSpeedMetersPerSecond!=5m)throw new Exception("MAVLink telemetry did not reach the latest API with expected units.");
        Console.WriteLine("PASS: MAVLink -> UDP -> Jetson gateway -> WebSocket -> PostgreSQL -> latest telemetry API");
        // Verify the same stream used by Live Monitoring includes this mission's data.
        using var streamTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var stream=await client.GetAsync($"/api/live/stream?missionId={mission}",HttpCompletionOption.ResponseHeadersRead,streamTimeout.Token);
        stream.EnsureSuccessStatusCode();
        using var reader=new StreamReader(await stream.Content.ReadAsStreamAsync(streamTimeout.Token));
        var found=false;
        while(!streamTimeout.IsCancellationRequested)
        {
            var line=await reader.ReadLineAsync(streamTimeout.Token);
            if(line is null)break;
            if(line.StartsWith("data:")&&line.Contains(mission.ToString(),StringComparison.OrdinalIgnoreCase)){found=true;break;}
        }
        if(!found)throw new Exception("Live stream did not provide the paired mission snapshot.");
        Console.WriteLine("PASS: live SSE endpoint receives paired drone telemetry");
        (await client.DeleteAsync($"/api/drone-connections/{pair.Connection.Id}")).EnsureSuccessStatusCode();
        Console.WriteLine("PASS: pilot can revoke the connection through HTTP");
    }
}
