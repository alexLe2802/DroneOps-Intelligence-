using System.Net;
using System.Text.Json;
using DroneOps.Application.Services.Storage;
using Microsoft.Extensions.Configuration;

internal static class UserMediaAccessChecks
{
    public static async Task RunAsync()
    {
        var handler=new CaptureHandler();
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["SupabaseStorage:Url"]="https://storage.invalid",["SupabaseStorage:ServiceKey"]="server-secret" }).Build();
        var storage=new UserMediaStorage(new HttpClient(handler),configuration);
        var signed=await storage.SignAsync($"avatars/{Guid.NewGuid():N}/{Guid.NewGuid():N}");
        Check(signed.Contains("token=fixture")&&handler.ExpiresIn==900,"private media URL expires after 15 minutes");
        try{await storage.SignAsync("public/avatar.png");throw new Exception("unsafe path accepted");}
        catch(ArgumentException){Check(true,"private media signer rejects paths outside owned prefixes");}
        Console.WriteLine("2 private media access checks passed.");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
    private sealed class CaptureHandler:HttpMessageHandler
    {
        public int ExpiresIn {get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));ExpiresIn=json.RootElement.GetProperty("expiresIn").GetInt32();
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"signedURL\":\"/object/sign/user-media/avatar?token=fixture\"}")};
        }
    }
}
