using System.Net;
using System.Text.Json;
using DroneOps.Application.Services.Storage;
using Microsoft.Extensions.Configuration;

internal static class UserMediaAccessChecks
{
    public static async Task RunAsync()
    {
        var localRoot = Path.Combine(Path.GetTempPath(), "droneops-media-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var localConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["UserMediaStorage:Provider"] = "Auto",
                    ["UserMediaStorage:LocalRoot"] = localRoot,
                    ["SupabaseStorage:ServiceKey"] = "sb_secret_fixture"
                }).Build();
            var localStorage = new UserMediaStorage(new HttpClient(), localConfiguration);
            var localPath = $"uavs/{Guid.NewGuid():N}/{Guid.NewGuid():N}.jpg";
            var fixture = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
            await using (var source = new MemoryStream(fixture))
                await localStorage.UploadAsync(localPath, source, "image/jpeg");
            var localUrl = await localStorage.SignAsync(localPath);
            var roundTrip = await localStorage.DownloadAsync(localPath);
            Check(localUrl == UserMediaStorage.LocalMediaScheme + localPath
                && roundTrip.SequenceEqual(fixture),
                "opaque Supabase secret falls back to local private media round-trip");
            await localStorage.DeleteAsync([localPath]);
            Check(!Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories).Any(),
                "local private media deletion removes the stored object");

            var blankRootConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["UserMediaStorage:Provider"] = "Local",
                    ["UserMediaStorage:LocalRoot"] = ""
                }).Build();
            var blankRootStorage = new UserMediaStorage(new HttpClient(), blankRootConfiguration);
            var blankRootPath = $"uavs/{Guid.NewGuid():N}/{Guid.NewGuid():N}.jpg";
            await using (var source = new MemoryStream(fixture))
                await blankRootStorage.UploadAsync(blankRootPath, source, "image/jpeg");
            Check((await blankRootStorage.DownloadAsync(blankRootPath)).SequenceEqual(fixture),
                "blank local media root uses the safe per-user default directory");
            await blankRootStorage.DeleteAsync([blankRootPath]);
        }
        finally
        {
            if (Directory.Exists(localRoot)) Directory.Delete(localRoot, true);
        }

        var incompleteConfiguration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["UserMediaStorage:Provider"]="Supabase",["SupabaseStorage:Url"]="https://storage.invalid",["SupabaseStorage:ServiceKey"]="sb_secret_fixture" }).Build();
        try
        {
            await new UserMediaStorage(new HttpClient(new CaptureHandler()),incompleteConfiguration)
                .SignAsync($"avatars/{Guid.NewGuid():N}/{Guid.NewGuid():N}");
            throw new Exception("incomplete Storage authorization accepted");
        }
        catch(InvalidOperationException)
        {
            Check(true,"opaque Supabase secret cannot silently reach Storage without JWT authorization");
        }
        var handler=new CaptureHandler();
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["SupabaseStorage:Url"]="https://storage.invalid",["SupabaseStorage:ServiceKey"]="sb_secret_fixture",["SupabaseStorage:AuthorizationToken"]="eyJ.fixture" }).Build();
        var storage=new UserMediaStorage(new HttpClient(handler),configuration);
        var signed=await storage.SignAsync($"avatars/{Guid.NewGuid():N}/{Guid.NewGuid():N}");
        Check(signed.Contains("token=fixture")&&handler.ExpiresIn==900,"private media URL expires after 15 minutes");
        try{await storage.SignAsync("public/avatar.png");throw new Exception("unsafe path accepted");}
        catch(ArgumentException){Check(true,"private media signer rejects paths outside owned prefixes");}
        Console.WriteLine("6 private media access checks passed.");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
    private sealed class CaptureHandler:HttpMessageHandler
    {
        public int ExpiresIn {get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if (request.Headers.Authorization?.Scheme != "Bearer" || request.Headers.Authorization.Parameter != "eyJ.fixture")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));ExpiresIn=json.RootElement.GetProperty("expiresIn").GetInt32();
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"signedURL\":\"/object/sign/user-media/avatar?token=fixture\"}")};
        }
    }
}
