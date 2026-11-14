using System.Net;
using System.Text;
using System.Text.Json;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Application.Interfaces.UAVs;
using DroneOps.Application.Services.Notifications;
using DroneOps.Application.Services.Storage;
using DroneOps.Application.Services.UAVs;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using DroneOps.Persistence.Repositories.Missions;
using DroneOps.Persistence.Repositories.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

internal static class UavImageChecks
{
    public static async Task<int> RunAsync()
    {
        var passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            Console.WriteLine("PASS: " + name);
            passed++;
        }

        var validator = new UserMediaStorage(
            new HttpClient(), new ConfigurationBuilder().AddInMemoryCollection().Build());
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
        await using (var valid = new MemoryStream(png))
            Check((await validator.ReadValidatedUavImageAsync(valid, "drone.png", "image/png", png.Length)).Length == png.Length,
                "UAV image validation accepts matching PNG MIME, extension and magic bytes");
        try
        {
            await using var disguised = new MemoryStream("not-an-image"u8.ToArray());
            await validator.ReadValidatedUavImageAsync(disguised, "drone.jpg", "image/jpeg", disguised.Length);
            throw new Exception("Disguised executable/text payload was accepted as an image.");
        }
        catch (ArgumentException) { Check(true, "UAV image validation rejects spoofed image content"); }

        var missingGemini = new UavImageModerationService(
            new HttpClient(new GeminiHandler(HttpStatusCode.OK, "")) { BaseAddress = new Uri("https://fixture.invalid/") },
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            NullLogger<UavImageModerationService>.Instance);
        Check((await missingGemini.ModerateAsync(png, "image/png")).Decision == UavImageModerationDecision.Unavailable,
            "missing Gemini configuration defers moderation instead of falsely rejecting the UAV image");

        var geminiConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UavImageModeration:ApiKey"] = "fixture-key",
            ["UavImageModeration:Model"] = "gemini-3.5-flash-lite"
        }).Build();
        var allowedGemini = new UavImageModerationService(
            new HttpClient(new GeminiHandler(HttpStatusCode.OK, "{\"isDrone\":true,\"containsInappropriateContent\":false}")) { BaseAddress = new Uri("https://fixture.invalid/") },
            geminiConfiguration,
            NullLogger<UavImageModerationService>.Instance);
        Check((await allowedGemini.ModerateAsync(png, "image/png")).Decision == UavImageModerationDecision.Approved,
            "Gemini accepts a genuine safe UAV image");

        var unsafeGemini = new UavImageModerationService(
            new HttpClient(new GeminiHandler(HttpStatusCode.OK, "{\"isDrone\":false,\"containsInappropriateContent\":true}")) { BaseAddress = new Uri("https://fixture.invalid/") },
            geminiConfiguration,
            NullLogger<UavImageModerationService>.Instance);
        Check((await unsafeGemini.ModerateAsync(png, "image/png")).Decision == UavImageModerationDecision.Rejected,
            "Gemini rejects non-UAV or inappropriate content");

        var unavailableGemini = new UavImageModerationService(
            new HttpClient(new GeminiHandler(HttpStatusCode.TooManyRequests, "")) { BaseAddress = new Uri("https://fixture.invalid/") },
            geminiConfiguration,
            NullLogger<UavImageModerationService>.Instance);
        Check((await unavailableGemini.ModerateAsync(png, "image/png")).Decision == UavImageModerationDecision.Unavailable,
            "Gemini service failure is distinguished from an unsafe image decision");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("uav-image-check-" + Guid.NewGuid()).Options;
        await using var db = new AppDbContext(options);
        var roleId = Guid.NewGuid();
        var managerRoleId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var pilotId = Guid.NewGuid();
        var uavId = Guid.NewGuid();
        db.Roles.AddRange(
            new Role { Id = roleId, Name = "uav_operator" },
            new Role { Id = managerRoleId, Name = "operation_manager" });
        db.Users.AddRange(
            new User { Id = ownerId, RoleId = roleId, FullName = "Owner", Email = "owner@fixture.test" },
            new User { Id = otherId, RoleId = roleId, FullName = "Other", Email = "other@fixture.test" },
            new User { Id = managerId, RoleId = managerRoleId, FullName = "Manager", Email = "manager@fixture.test" });
        db.Pilots.Add(new Pilot { Id = pilotId, UserId = ownerId, PilotLicenseNo = "FIXTURE" });
        db.UAVs.Add(new UAV { Id = uavId, PilotId = pilotId, Code = "IMG-1", Name = "Image fixture", Status = "Active" });
        await db.SaveChangesAsync();

        var storage = new TrackingStorage();
        var enabledModerationConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["UavImageModeration:Enabled"] = "true" }).Build();
        INotificationService notifications = new NotificationService(
            new NotificationRepository(db), new MissionRepository(db));
        var service = new UavService(
            new UavRepository(db), new PilotRepository(db), notifications,
            NullLogger<UavService>.Instance, storage, new AllowModeration(),
            enabledModerationConfiguration, new UavReviewWorkflow(db));

        await service.UpdateAsync(ownerId, uavId,
            new DroneOps.Application.DTOs.Request.UAVs.UpdateUavRequest
            {
                ExpectedVersion = 0,
                Code = "IMG-1",
                Name = "Image fixture"
            });
        Check((await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId)).Status == "Active",
            "unchanged edit DTO before image upload preserves the UAV operational status");

        await using (var first = new MemoryStream(png))
            await service.UploadOrReplaceUavImageAsync(
                uavId, ownerId, "uav_operator", first, "first.png", "image/png", png.Length);
        var firstPath = (await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId)).ImageStoragePath;
        Check(firstPath is not null && storage.Paths.SetEquals([firstPath]), "first UAV image is stored and referenced");
        Check((await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId)).ImageModerationStatus == "Pending",
            "UAV image upload is queued for durable asynchronous moderation");
        Check((await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId)).Status == "Active",
            "optional image upload preserves the UAV operational status");

        await using (var second = new MemoryStream(png))
            await service.UploadOrReplaceUavImageAsync(
                uavId, ownerId, "uav_operator", second, "second.png", "image/png", png.Length);
        var secondPath = (await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId)).ImageStoragePath;
        Check(secondPath is not null && secondPath != firstPath && storage.Paths.SetEquals([secondPath]) && storage.Deleted.Contains(firstPath!),
            "second UAV image replaces the first and removes the old bucket object");

        try
        {
            await using var unauthorized = new MemoryStream(png);
            await service.UploadOrReplaceUavImageAsync(
                uavId, otherId, "uav_operator", unauthorized, "other.png", "image/png", png.Length);
            throw new Exception("Another pilot replaced the UAV image.");
        }
        catch (UnauthorizedAccessException) { Check(true, "another pilot cannot replace an owned UAV image"); }

        var unavailableService = new UavService(
            new UavRepository(db), new PilotRepository(db), notifications,
            NullLogger<UavService>.Instance, storage, new UnavailableModeration(),
            enabledModerationConfiguration);
        await using (var deferred = new MemoryStream(png))
            await unavailableService.UploadOrReplaceUavImageAsync(
                uavId, ownerId, "uav_operator", deferred, "deferred.png", "image/png", png.Length);
        Check((await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId)).ImageModerationStatus == "Pending",
            "provider outage never falsely rejects a valid upload");
        Check((await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId)).Status == "Active",
            "image moderation outage does not deactivate the UAV");

        var automaticUavId = Guid.NewGuid();
        db.UAVs.Add(new UAV
        {
            Id = automaticUavId, PilotId = pilotId, Code = "IMG-AUTO", Name = "Automatic image fixture",
            Status = "Active"
        });
        await db.SaveChangesAsync();
        var disabledModerationConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["UavImageModeration:Enabled"] = "false" }).Build();
        var automaticService = new UavService(
            new UavRepository(db), new PilotRepository(db), notifications,
            NullLogger<UavService>.Instance, storage, new UnavailableModeration(),
            disabledModerationConfiguration);
        await using (var automatic = new MemoryStream(png))
            await automaticService.UploadOrReplaceUavImageAsync(
                automaticUavId, ownerId, "uav_operator", automatic, "automatic.png", "image/png", png.Length);
        var automaticallyApproved = await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == automaticUavId);
        Check(automaticallyApproved.ImageModerationStatus == "Approved" &&
                automaticallyApproved.Status == "Active" &&
                automaticallyApproved.ImageModerationNextAttemptAt == null,
            "disabled AI accepts the optional image immediately without changing UAV status");

        await automaticService.RejectUavImageAsync(automaticUavId, "Image does not show the registered aircraft.");
        var manuallyRejected = await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == automaticUavId);
        Check(manuallyRejected.ImageStoragePath == null &&
                manuallyRejected.ImageModerationStatus == "Rejected" &&
                manuallyRejected.Status == "Active" &&
                manuallyRejected.ImageModerationReason == "Image does not show the registered aircraft.",
            "manager image rejection removes only the image and preserves UAV operations");
        Check(await db.Notifications.AnyAsync(x => x.RecipientUserId == ownerId &&
                x.Type == "UavImageRejected" && x.Message.Contains("Image does not show")),
            "manager image rejection notifies the owner with the reason");

        var firstManual = await db.UAVs.SingleAsync(x => x.Id == uavId);
        firstManual.ImageModerationStatus = "ManualReview";
        await db.SaveChangesAsync();
        await notifications.RefreshUavManualReviewAsync();
        await notifications.RefreshUavManualReviewAsync();
        Check(await db.Notifications.CountAsync(x => x.Type == "UavManualReview" &&
                x.ReferenceType == "UAVManualReviewQueueActive") == 1,
            "manual-review queue creates one aggregate manager notification without duplicates");

        var secondUavId = Guid.NewGuid();
        db.UAVs.Add(new UAV
        {
            Id = secondUavId, PilotId = pilotId, Code = "IMG-2", Name = "Second manual fixture",
            Status = "Active", ImageStoragePath = "uavs/fixture/second.png",
            ImageModerationStatus = "ManualReview"
        });
        await db.SaveChangesAsync();
        await notifications.RefreshUavManualReviewAsync();
        var aggregate = await db.Notifications.SingleAsync(x => x.Type == "UavManualReview" &&
            x.ReferenceType == "UAVManualReviewQueueActive");
        Check(aggregate.Title.StartsWith("2 UAVs", StringComparison.Ordinal) &&
                aggregate.ActionUrl == "/uav-management?imageModerationStatus=ManualReview",
            "manual-review aggregate updates its count and links to the filtered queue");
        var manualQueue = await new UavRepository(db).GetAllPagedAsync(
            null, null, "ManualReview", 1, 10);
        Check(manualQueue.TotalCount == 2 && manualQueue.Items.All(x => x.ImageModerationStatus == "ManualReview"),
            "manager UAV API can filter the manual-review queue server-side");

        firstManual.ImageModerationStatus = "Approved";
        (await db.UAVs.SingleAsync(x => x.Id == secondUavId)).ImageModerationStatus = "Rejected";
        await db.SaveChangesAsync();
        await notifications.RefreshUavManualReviewAsync();
        aggregate = await db.Notifications.SingleAsync(x => x.Type == "UavManualReview");
        Check(aggregate.IsRead && aggregate.ReferenceType == "UAVManualReviewQueueClosed",
            "manual-review aggregate closes automatically after all items are resolved");

        Check(await service.DeleteUavImageAsync(uavId, ownerId, "uav_operator") &&
                (await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId)).Status == "Active",
            "optional image deletion preserves the UAV operational status");

        return passed;
    }

    private sealed class AllowModeration : IUavImageModerationService
    {
        public Task<UavImageModerationResult> ModerateAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UavImageModerationResult(UavImageModerationDecision.Approved, "fixture"));
    }

    private sealed class UnavailableModeration : IUavImageModerationService
    {
        public Task<UavImageModerationResult> ModerateAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UavImageModerationResult(UavImageModerationDecision.Unavailable, "fixture outage"));
    }

    private sealed class TrackingStorage : IUserMediaStorage
    {
        public HashSet<string> Paths { get; } = [];
        public HashSet<string> Deleted { get; } = [];
        public Task ValidateAsync(IFormFile file, bool avatar, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UploadAsync(string path, IFormFile file, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<byte[]> ReadValidatedUavImageAsync(Stream stream, string fileName, string contentType, long fileSize, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        public Task UploadAsync(string path, Stream stream, string contentType, CancellationToken cancellationToken = default)
        {
            Paths.Add(path);
            return Task.CompletedTask;
        }
        public Task<string> SignAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult("https://fixture.invalid/" + path);
        public Task<byte[]> DownloadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(pngFixture);
        private static readonly byte[] pngFixture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        public Task DeleteAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        {
            foreach (var path in paths) { Paths.Remove(path); Deleted.Add(path); }
            return Task.CompletedTask;
        }
    }

    private sealed class GeminiHandler(HttpStatusCode statusCode, string resultJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var responseBody = string.IsNullOrWhiteSpace(resultJson)
                ? "{}"
                : JsonSerializer.Serialize(new
                {
                    candidates = new[] { new { content = new { parts = new[] { new { text = resultJson } } } } }
                });
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
