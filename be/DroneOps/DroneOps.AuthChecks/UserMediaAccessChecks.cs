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
                    ["UserMediaStorage:Provider"] = "Local",
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
                "explicit Local provider supports private media round-trip");
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

        // Every network operation must use Supabase even with an opaque secret key.
        foreach (var missingPath in new[] { $"licenses/{Guid.NewGuid():N}/{Guid.NewGuid():N}.pdf", $"avatars/{Guid.NewGuid():N}/{Guid.NewGuid():N}", $"uavs/{Guid.NewGuid():N}/{Guid.NewGuid():N}.jpg" })
        {
            var missingConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["UserMediaStorage:Provider"] = "Local", ["UserMediaStorage:LocalRoot"] = localRoot }).Build();
            try { await new UserMediaStorage(new HttpClient(), missingConfiguration).SignAsync(missingPath); throw new Exception("missing file accepted"); }
            catch (HttpRequestException ex) { Check(ex.StatusCode == HttpStatusCode.NotFound, "missing media returns a recoverable storage error"); }
        }
        foreach (var path in new[] { $"uavs/{Guid.NewGuid():N}/{Guid.NewGuid():N}.jpg", $"licenses/{Guid.NewGuid():N}/{Guid.NewGuid():N}.pdf", $"licenses/{Guid.NewGuid():N}/{Guid.NewGuid():N}.docx", $"avatars/{Guid.NewGuid():N}/{Guid.NewGuid():N}" })
        {
        foreach (var provider in new string?[] { null, "Supabase" })
        foreach (var key in new[] { "sb_secret_fixture", "eyJ.fixture" })
        {
            var handler = new CaptureHandler(key, key.StartsWith("eyJ") ? key : null);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UserMediaStorage:Provider"] = provider,
                ["UserMediaStorage:LocalRoot"] = localRoot,
                ["SupabaseStorage:Url"] = "https://storage.invalid",
                ["SupabaseStorage:ServiceKey"] = key
            }).Build();
            var storage = new UserMediaStorage(new HttpClient(handler), configuration);
            await storage.UploadAsync(path, new MemoryStream([0xFF, 0xD8, 0xFF]), "image/jpeg");
            await storage.UploadAsync(path, new Microsoft.AspNetCore.Http.FormFile(
                new MemoryStream([0xFF, 0xD8, 0xFF]), 0, 3, "file", "fixture.jpg")
                { Headers = new Microsoft.AspNetCore.Http.HeaderDictionary(), ContentType = "image/jpeg" });
            var signed = await storage.SignAsync(path);
            Check(signed.StartsWith("https://storage.invalid/storage/v1/object/sign/") && handler.ExpiresIn == 900,
                "Supabase signing uses the configured host and 15-minute expiry");
            if (path.StartsWith("uavs/"))
                Check((await storage.DownloadAsync(path)).SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }),
                    "Supabase download returns the stored image bytes");
            await storage.DeleteAsync([path]);
            Check(handler.Requests == (path.StartsWith("uavs/") ? 5 : 4) && !Directory.Exists(localRoot),
                "upload, sign, download and delete use Supabase without local fallback");
            try { await storage.SignAsync("public/avatar.png"); throw new Exception("unsafe path accepted"); }
            catch (ArgumentException) { Check(true, "private media signer rejects paths outside owned prefixes"); }
        }

        }
        var checkPath = $"uavs/{Guid.NewGuid():N}/{Guid.NewGuid():N}.jpg";
        foreach (var provider in new[] { "Auto", "Suapbase", "" })
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["UserMediaStorage:Provider"] = provider }).Build();
            try { await new UserMediaStorage(new HttpClient(), configuration).SignAsync(checkPath); throw new Exception("invalid provider accepted"); }
            catch (InvalidOperationException) { Check(true, "invalid provider fails explicitly"); }
        }
        foreach (var key in new string?[] { null, "", "sb_publishable_fixture", "invalid" })
        {
            var handler = new CaptureHandler("unused", null);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["SupabaseStorage:ServiceKey"] = key }).Build();
            try { await new UserMediaStorage(new HttpClient(handler), configuration).SignAsync(checkPath); throw new Exception("invalid server credential accepted"); }
            catch (InvalidOperationException) { Check(handler.Requests == 0, "missing or public credentials fail before contacting storage"); }
        }
        var overrideHandler = new CaptureHandler("sb_secret_fixture", "eyJ.override");
        var overrideConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["SupabaseStorage:Url"] = "https://storage.invalid", ["SupabaseStorage:ServiceKey"] = "sb_secret_fixture", ["SupabaseStorage:AuthorizationToken"] = "eyJ.override" }).Build();
        await new UserMediaStorage(new HttpClient(overrideHandler), overrideConfiguration).SignAsync(checkPath);
        Check(overrideHandler.Requests == 1, "explicit legacy authorization override remains supported");

        var failingHandler = new CaptureHandler("sb_secret_fixture", null) { Fail = true };
        var failingConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["SupabaseStorage:ServiceKey"] = "sb_secret_fixture", ["UserMediaStorage:LocalRoot"] = localRoot }).Build();
        try { await new UserMediaStorage(new HttpClient(failingHandler), failingConfiguration).UploadAsync(checkPath, new MemoryStream([1]), "image/jpeg"); throw new Exception("storage failure swallowed"); }
        catch (HttpRequestException) { Check(!Directory.Exists(localRoot), "Supabase failure never writes to local storage"); }
        Console.WriteLine("Private media access checks passed.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private sealed class CaptureHandler(string key, string? bearer) : HttpMessageHandler
    {
        public int ExpiresIn { get; private set; }
        public int Requests { get; private set; }
        public bool Fail { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Check(request.Headers.GetValues("apikey").Single() == key && request.Headers.Authorization?.Parameter == bearer
                && (bearer is null || request.Headers.Authorization?.Scheme == "Bearer"), "Storage headers match the server credential type");
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (request.RequestUri!.AbsolutePath.Contains("/object/sign/"))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                ExpiresIn = json.RootElement.GetProperty("expiresIn").GetInt32();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"signedURL\":\"/object/sign/user-media/fixture?token=fixture\"}") };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF]) };
        }
    }
}
