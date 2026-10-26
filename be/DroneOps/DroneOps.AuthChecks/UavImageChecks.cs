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

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("uav-image-check-" + Guid.NewGuid()).Options;
        await using var db = new AppDbContext(options);
        var roleId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var pilotId = Guid.NewGuid();
        var uavId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, Name = "uav_operator" });
        db.Users.AddRange(
            new User { Id = ownerId, RoleId = roleId, FullName = "Owner", Email = "owner@fixture.test" },
            new User { Id = otherId, RoleId = roleId, FullName = "Other", Email = "other@fixture.test" });
        db.Pilots.Add(new Pilot { Id = pilotId, UserId = ownerId, PilotLicenseNo = "FIXTURE" });
        db.UAVs.Add(new UAV { Id = uavId, PilotId = pilotId, Code = "IMG-1", Name = "Image fixture", Status = "Active" });
        await db.SaveChangesAsync();

        var storage = new TrackingStorage();
        INotificationService notifications = new NotificationService(
            new NotificationRepository(db), new MissionRepository(db));
        var service = new UavService(
            new UavRepository(db), new PilotRepository(db), notifications,
            NullLogger<UavService>.Instance, storage, new AllowModeration());

        await using (var first = new MemoryStream(png))
            await service.UploadOrReplaceUavImageAsync(
                uavId, ownerId, "uav_operator", first, "first.png", "image/png", png.Length);
        var firstPath = (await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == uavId)).ImageStoragePath;
        Check(firstPath is not null && storage.Paths.SetEquals([firstPath]), "first UAV image is stored and referenced");

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

        var rejectingService = new UavService(
            new UavRepository(db), new PilotRepository(db), notifications,
            NullLogger<UavService>.Instance, storage, new RejectModeration());
        try
        {
            await using var rejected = new MemoryStream(png);
            await rejectingService.UploadOrReplaceUavImageAsync(
                uavId, ownerId, "uav_operator", rejected, "unsafe.png", "image/png", png.Length);
            throw new Exception("AI-rejected image was stored.");
        }
        catch (ArgumentException) { Check(true, "AI moderation rejection blocks storage and database replacement"); }

        return passed;
    }

    private sealed class AllowModeration : IUavImageModerationService
    {
        public Task<bool> IsAllowedAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RejectModeration : IUavImageModerationService
    {
        public Task<bool> IsAllowedAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken = default) => Task.FromResult(false);
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
        public Task DeleteAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        {
            foreach (var path in paths) { Paths.Remove(path); Deleted.Add(path); }
            return Task.CompletedTask;
        }
    }
}
