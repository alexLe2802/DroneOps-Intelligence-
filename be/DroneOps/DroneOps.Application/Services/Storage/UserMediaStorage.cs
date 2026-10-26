using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DroneOps.Application.Interfaces.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace DroneOps.Application.Services.Storage;

public sealed class UserMediaStorage : IUserMediaStorage
{
    private const string Bucket = "user-media";

    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;

    public UserMediaStorage(
        HttpClient client,
        IConfiguration configuration)
    {
        _client = client;
        _configuration = configuration;
    }

    private string BaseUrl =>
        (_configuration["SupabaseStorage:Url"]
            ?? "https://diaswbzjhvzwxcadlias.supabase.co")
        .TrimEnd('/') + "/storage/v1";

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path)
    {
        string? key =
            _configuration["SupabaseStorage:ServiceKey"];

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Supabase Storage is not configured. " +
                "Set SupabaseStorage__ServiceKey on the backend.");
        }

        if (key.StartsWith("sb_publishable_"))
        {
            throw new InvalidOperationException(
                "Storage uploads require a server secret key " +
                "or service_role key, not a publishable key.");
        }

        var request = new HttpRequestMessage(
            method,
            BaseUrl + path);

        request.Headers.Add("apikey", key);

        // Legacy service_role keys are JWT values.
        if (key.StartsWith("eyJ"))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    key);
        }

        return request;
    }

    public async Task ValidateAsync(IFormFile file, bool avatar, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > (avatar ? 5 : 10) * 1024 * 1024)
            throw new ArgumentException(avatar ? "Avatar must be 5 MB or smaller." : "Each license file must be 10 MB or smaller.");
        await using var stream = file.OpenReadStream();
        var bytes = new byte[12];
        var count = await stream.ReadAtLeastAsync(bytes, 12, false, ct);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var valid = file.ContentType switch
        {
            "image/jpeg" => avatar && count >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
            "image/png" => avatar && count >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}),
            "image/webp" => avatar && count >= 12 && bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WEBP"u8),
            "application/pdf" => !avatar && extension == ".pdf" && count >= 5 && bytes.AsSpan(0,5).SequenceEqual("%PDF-"u8),
            "application/msword" => !avatar && extension == ".doc" && count >= 8
                && bytes.AsSpan(0,8).SequenceEqual(new byte[] {208,207,17,224,161,177,26,225}),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => !avatar && extension == ".docx"
                && await IsWordArchiveAsync(file, ct),
            _ => false
        };
        if (!valid) throw new ArgumentException(avatar ? "Choose a valid JPEG, PNG or WebP avatar." : "Choose one valid PDF or Word (.doc, .docx) license document.");
    }

    private static async Task<bool> IsWordArchiveAsync(IFormFile file, CancellationToken ct)
    {
        try
        {
            await using var source = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
            return archive.GetEntry("[Content_Types].xml") is not null
                && archive.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException) { return false; }
    }

    public async Task UploadAsync(
        string path,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request =
            CreateRequest(
                HttpMethod.Post,
                $"/object/{Bucket}/{path}");

        await using Stream stream =
            file.OpenReadStream();

        request.Content =
            new StreamContent(stream);

        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue(
                file.ContentType);

        using HttpResponseMessage response =
            await _client.SendAsync(
                request,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                "Unable to upload file to Supabase Storage.");
        }
    }

    public async Task<byte[]> ReadValidatedUavImageAsync(
        Stream stream,
        string fileName,
        string contentType,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        const long maximumSize = 5 * 1024 * 1024;
        if (fileSize <= 0 || fileSize > maximumSize)
        {
            throw new ArgumentException("UAV image must be 5 MB or smaller.");
        }

        var normalizedContentType = contentType.Trim().ToLowerInvariant();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var validExtension = normalizedContentType switch
        {
            "image/jpeg" => extension is ".jpg" or ".jpeg",
            "image/png" => extension == ".png",
            "image/webp" => extension == ".webp",
            _ => false
        };
        if (!validExtension)
        {
            throw new ArgumentException("Choose a JPEG, PNG or WebP UAV image with a matching file extension.");
        }

        using var buffer = new MemoryStream((int)fileSize);
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != fileSize || buffer.Length > maximumSize)
        {
            throw new ArgumentException("The UAV image payload size is invalid.");
        }

        var bytes = buffer.ToArray();
        var signatureMatches = normalizedContentType switch
        {
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/webp" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!signatureMatches)
        {
            throw new ArgumentException("The uploaded file signature does not match a valid JPEG, PNG or WebP image.");
        }

        return bytes;
    }

    public async Task UploadAsync(
        string path,
        Stream stream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, $"/object/{Bucket}/{path}");
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Unable to upload file to Supabase Storage.");
        }
    }

    public async Task<string> SignAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        bool isValidPath = Regex.IsMatch(
            path,
            @"^(licenses|avatars|uavs)/" +
            @"[a-fA-F0-9-]+/" +
            @"[a-fA-F0-9]+" +
            @"(\.(pdf|doc|docx|png|webp|jpg))?$");

        if (!isValidPath)
        {
            throw new ArgumentException(
                "Invalid stored media path.");
        }

        using HttpRequestMessage request =
            CreateRequest(
                HttpMethod.Post,
                $"/object/sign/{Bucket}/{path}");

        request.Content = JsonContent.Create(
            new
            {
                expiresIn = 900
            });

        using HttpResponseMessage response =
            await _client.SendAsync(
                request,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                "Unable to open stored file.");
        }

        JsonElement json =
            await response.Content
                .ReadFromJsonAsync<JsonElement>(
                    cancellationToken);

        string? signedUrl =
            json.GetProperty("signedURL")
                .GetString();

        if (string.IsNullOrWhiteSpace(signedUrl))
        {
            throw new HttpRequestException(
                "Supabase Storage did not return a signed URL.");
        }

        return BaseUrl + signedUrl;
    }

    public async Task DeleteAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        string[] pathList =
            paths.ToArray();

        if (pathList.Length == 0)
        {
            return;
        }

        using HttpRequestMessage request =
            CreateRequest(
                HttpMethod.Delete,
                $"/object/{Bucket}");

        request.Content = JsonContent.Create(
            new
            {
                prefixes = pathList
            });

        using HttpResponseMessage response =
            await _client.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
