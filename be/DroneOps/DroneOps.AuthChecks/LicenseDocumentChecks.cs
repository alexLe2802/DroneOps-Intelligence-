using System.IO.Compression;
using DroneOps.Application.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;

internal static class LicenseDocumentChecks
{
    public static async Task RunAsync()
    {
        var storage = new UserMediaStorage(new HttpClient(), new ConfigurationBuilder().Build());
        async Task Check(string name, string mime, byte[] bytes, bool accepted, bool avatar = false)
        {
            using var stream = new MemoryStream(bytes);
            var file = new FormFile(stream, 0, bytes.Length, "files", name) { Headers = new HeaderDictionary(), ContentType = mime };
            try
            {
                await storage.ValidateAsync(file, avatar, default);
                if (!accepted) throw new Exception("Unexpectedly accepted: " + name);
            }
            catch (ArgumentException) when (!accepted) { }
            Console.WriteLine("PASS " + name);
        }
        await Check("license.pdf", "application/pdf", "%PDF-1.7 test"u8.ToArray(), true);
        await Check("license.doc", "application/msword", new byte[] {208,207,17,224,161,177,26,225,0,0,0,0}, true);
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            zip.CreateEntry("[Content_Types].xml");
            zip.CreateEntry("word/document.xml");
        }
        await Check("license.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", buffer.ToArray(), true);
        await Check("fake.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "not a document"u8.ToArray(), false);
        await Check("fake.pdf", "application/pdf", buffer.ToArray(), false);
        await Check("photo.png", "image/png", new byte[] {137,80,78,71,13,10,26,10,0,0,0,0}, true);
        await Check("avatar.png", "image/png", new byte[] {137,80,78,71,13,10,26,10,0,0,0,0}, true, true);
        await Check("large.png", "image/png", new byte[5 * 1024 * 1024 + 1], false);
        await Check("fake.png", "image/png", "not an image"u8.ToArray(), false);
        await Check("empty.pdf", "application/pdf", Array.Empty<byte>(), false);
        await Check("large.pdf", "application/pdf", new byte[10 * 1024 * 1024 + 1], false);
    }
}
