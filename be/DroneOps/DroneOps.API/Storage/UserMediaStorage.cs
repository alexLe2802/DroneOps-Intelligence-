using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DroneOps.API.Storage;

public sealed class UserMediaStorage(HttpClient client, IConfiguration configuration)
{
    private const string Bucket = "user-media";
    private string BaseUrl => (configuration["SupabaseStorage:Url"] ?? "https://diaswbzjhvzwxcadlias.supabase.co").TrimEnd('/') + "/storage/v1";

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var key = configuration["SupabaseStorage:ServiceKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Supabase Storage is not configured. Set SupabaseStorage__ServiceKey on the backend.");
        if (key.StartsWith("sb_publishable_")) throw new InvalidOperationException("Storage uploads require a server secret key or service_role key, not a publishable key.");
        var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Add("apikey", key);
        // Modern secret keys authenticate via apikey; legacy service_role keys are JWTs.
        if (key.StartsWith("eyJ")) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    public static async Task ValidateAsync(IFormFile file, bool avatar, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > (avatar ? 5 : 10) * 1024 * 1024)
            throw new ArgumentException(avatar ? "Avatar must be 5 MB or smaller." : "Each license file must be 10 MB or smaller.");
        await using var stream = file.OpenReadStream();
        var bytes = new byte[12];
        var count = await stream.ReadAtLeastAsync(bytes, 12, false, ct);
        var valid = file.ContentType switch
        {
            "image/jpeg" => count >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
            "image/png" => count >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}),
            "image/webp" => count >= 12 && bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WEBP"u8),
            "application/pdf" => !avatar && count >= 5 && bytes.AsSpan(0,5).SequenceEqual("%PDF-"u8),
            _ => false
        };
        if (!valid) throw new ArgumentException("Unsupported file. Choose a valid JPEG, PNG, WebP image or a license PDF.");
    }

    public async Task UploadAsync(string path, IFormFile file, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, $"/object/{Bucket}/{path}");
        await using var stream = file.OpenReadStream();
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Unable to upload file to Supabase Storage.");
    }

    public async Task<string> SignAsync(string path, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(path, @"^(licenses|avatars)/[a-fA-F0-9-]+/[a-fA-F0-9]+(\.(pdf|png|webp|jpg))?$")) throw new ArgumentException("Invalid stored media path.");
        using var request = Request(HttpMethod.Post, $"/object/sign/{Bucket}/{path}");
        request.Content = JsonContent.Create(new { expiresIn = 900 });
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Unable to open stored file.");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return BaseUrl + json.GetProperty("signedURL").GetString();
    }

    public async Task DeleteAsync(IEnumerable<string> paths, CancellationToken ct)
    {
        var list = paths.ToArray();
        if (list.Length == 0) return;
        using var request = Request(HttpMethod.Delete, $"/object/{Bucket}");
        request.Content = JsonContent.Create(new { prefixes = list });
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
