using System.Security.Claims;
using DroneOps.API.Controllers.UAVs;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Application.Services.Notifications;
using DroneOps.Application.Services.Storage;
using DroneOps.Application.Services.UAVs;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using DroneOps.Persistence.Repositories.Missions;
using DroneOps.Persistence.Repositories.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        INotificationService notifications = new NotificationService(
            new NotificationRepository(db), new MissionRepository(db));
        var service = new UavService(
            new UavRepository(db), new PilotRepository(db), notifications,
            NullLogger<UavService>.Instance, storage, new UavReviewWorkflow(db));

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
        Check((await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId)).ImageModerationStatus is null,
            "UAV image upload is published without a manager approval state");
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

        Check(await service.DeleteUavImageAsync(uavId, ownerId, "uav_operator"),
            "owner can delete their published UAV image");
        var afterDelete = await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId);
        Check(afterDelete.ImageStoragePath is null && afterDelete.ImageModerationStatus is null &&
                afterDelete.Status == "Active" && storage.Deleted.Contains(secondPath!),
            "deleting UAV image clears storage reference and preserves operational status");

        await using (var third = new MemoryStream(png))
            await service.UploadOrReplaceUavImageAsync(
                uavId, ownerId, "uav_operator", third, "third.png", "image/png", png.Length);

        await service.RejectUavImageAsync(uavId, managerId, "Inappropriate image content.");
        var manuallyRejected = await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId);
        Check(manuallyRejected.ImageStoragePath == null &&
                manuallyRejected.ImageModerationStatus == "Rejected" &&
                manuallyRejected.Status == "Active" &&
                manuallyRejected.ImageModerationReason == "Inappropriate image content.",
            "manager removes the published image without changing UAV operations");
        Check(await db.Notifications.AnyAsync(x => x.RecipientUserId == ownerId &&
                x.Type == "UavImageRejected" && x.Message.Contains("Inappropriate image content")),
            "manager image rejection notifies the owner with the reason");

        var misconfiguredService = new UavService(
            new UavRepository(db), new PilotRepository(db), notifications,
            NullLogger<UavService>.Instance, validator, new UavReviewWorkflow(db));
        var controller = new UavController(misconfiguredService, validator, NullLogger<UavController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, ownerId.ToString()),
                        new Claim(ClaimTypes.Role, "uav_operator")
                    ], "TestAuth"))
                }
            }
        };
        var formFile = new FormFile(new MemoryStream(png), 0, png.Length, "file", "drone.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        var misconfiguredResult = await controller.UploadOrReplaceImage(uavId, formFile, CancellationToken.None) as ObjectResult;
        var misconfiguredMessage = misconfiguredResult?.Value?.GetType().GetProperty("message")?.GetValue(misconfiguredResult.Value)?.ToString() ?? string.Empty;
        Check(misconfiguredResult?.StatusCode == 503 &&
                misconfiguredMessage.Contains("server credential", StringComparison.OrdinalIgnoreCase) &&
                !misconfiguredMessage.Contains("AI review", StringComparison.OrdinalIgnoreCase),
            "storage misconfiguration returns HTTP 503 without obsolete AI review messaging");

        return passed;
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
}
