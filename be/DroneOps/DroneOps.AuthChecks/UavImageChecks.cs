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
using System.Net;
using System.Text;
using System.Text.Json;

internal static class UavImageChecks
{
    private static readonly byte[] PngFixture =
    [
        0x89, 0x50, 0x4E, 0x47,
        0x0D, 0x0A, 0x1A, 0x0A,
        0x01, 0x02, 0x03, 0x04
    ];

    public static async Task<int> RunAsync()
    {
        var passed = 0;

        void Check(bool condition, string name)
        {
            if (!condition)
            {
                throw new InvalidOperationException("FAIL: " + name);
            }

            Console.WriteLine("PASS: " + name);
            passed++;
        }

        await RunValidationChecksAsync(Check);
        await RunGeminiChecksAsync(Check);
        await RunStorageWorkflowChecksAsync(Check);

        return passed;
    }

    // ============================================================
    // 1. IMAGE VALIDATION
    // ============================================================

    private static async Task RunValidationChecksAsync(
        Action<bool, string> check)
    {
        var validator = new UserMediaStorage(
            new HttpClient(),
            new ConfigurationBuilder()
                .AddInMemoryCollection()
                .Build());

        await using (var valid = new MemoryStream(PngFixture))
        {
            var bytes = await validator.ReadValidatedUavImageAsync(
                valid,
                "drone.png",
                "image/png",
                PngFixture.Length);

            check(
                bytes.Length == PngFixture.Length,
                "UAV image validation accepts matching PNG MIME, extension and magic bytes");
        }

        try
        {
            await using var disguised =
                new MemoryStream("not-an-image"u8.ToArray());

            await validator.ReadValidatedUavImageAsync(
                disguised,
                "drone.jpg",
                "image/jpeg",
                disguised.Length);

            throw new InvalidOperationException(
                "Disguised text payload was accepted as an image.");
        }
        catch (ArgumentException)
        {
            check(
                true,
                "UAV image validation rejects spoofed image content");
        }
    }

    // ============================================================
    // 2. GEMINI IMAGE MODERATION
    // ============================================================

    private static async Task RunGeminiChecksAsync(
        Action<bool, string> check)
    {
        // --------------------------------------------------------
        // Missing configuration
        // --------------------------------------------------------

        var emptyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        var missingGemini = CreateModerationService(
            HttpStatusCode.OK,
            string.Empty,
            emptyConfiguration);

        var missingResult = await missingGemini.ModerateAsync(
            PngFixture,
            "image/png");

        check(
            missingResult.Decision ==
            UavImageModerationDecision.Unavailable,
            "missing Gemini configuration reports moderation as unavailable");

        // --------------------------------------------------------
        // Configured Gemini
        // --------------------------------------------------------

        var geminiConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["UavImageModeration:ApiKey"] = "fixture-key",
                    ["UavImageModeration:Model"] =
                        "gemini-3.5-flash-lite"
                })
            .Build();

        // --------------------------------------------------------
        // Safe UAV image
        // --------------------------------------------------------

        var allowedGemini = CreateModerationService(
            HttpStatusCode.OK,
            """
            {
                "isDrone": true,
                "containsInappropriateContent": false
            }
            """,
            geminiConfiguration);

        var allowedResult = await allowedGemini.ModerateAsync(
            PngFixture,
            "image/png");

        check(
            allowedResult.Decision ==
            UavImageModerationDecision.Approved,
            "Gemini accepts a genuine safe UAV image");

        // --------------------------------------------------------
        // Unsafe / invalid image
        // --------------------------------------------------------

        var unsafeGemini = CreateModerationService(
            HttpStatusCode.OK,
            """
            {
                "isDrone": false,
                "containsInappropriateContent": true
            }
            """,
            geminiConfiguration);

        var unsafeResult = await unsafeGemini.ModerateAsync(
            PngFixture,
            "image/png");

        check(
            unsafeResult.Decision ==
            UavImageModerationDecision.Rejected,
            "Gemini rejects non-UAV or inappropriate content");

        // --------------------------------------------------------
        // Provider unavailable
        // --------------------------------------------------------

        var unavailableGemini = CreateModerationService(
            HttpStatusCode.TooManyRequests,
            string.Empty,
            geminiConfiguration);

        var unavailableResult =
            await unavailableGemini.ModerateAsync(
                PngFixture,
                "image/png");

        check(
            unavailableResult.Decision ==
            UavImageModerationDecision.Unavailable,
            "Gemini service failure is distinguished from an unsafe image decision");
    }

    private static UavImageModerationService CreateModerationService(
        HttpStatusCode statusCode,
        string resultJson,
        IConfiguration configuration)
    {
        var client = new HttpClient(
            new GeminiHandler(
                statusCode,
                resultJson))
        {
            BaseAddress =
                new Uri("https://fixture.invalid/")
        };

        return new UavImageModerationService(
            client,
            configuration,
            NullLogger<UavImageModerationService>.Instance);
    }

    // ============================================================
    // 3. UAV IMAGE STORAGE / REPLACEMENT / PERMISSION
    // ============================================================

    private static async Task RunStorageWorkflowChecksAsync(
        Action<bool, string> check)
    {
        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(
                    "uav-image-check-" + Guid.NewGuid())
                .Options;

        await using var db =
            new AppDbContext(options);

        // --------------------------------------------------------
        // Test data IDs
        // --------------------------------------------------------

        var operatorRoleId = Guid.NewGuid();

        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var pilotId = Guid.NewGuid();
        var uavId = Guid.NewGuid();

        // --------------------------------------------------------
        // Role
        // --------------------------------------------------------

        db.Roles.Add(
            new Role
            {
                Id = operatorRoleId,
                Name = "uav_operator"
            });

        // --------------------------------------------------------
        // Users
        // --------------------------------------------------------

        db.Users.AddRange(
            new User
            {
                Id = ownerId,
                RoleId = operatorRoleId,
                FullName = "Owner",
                Email = "owner@fixture.test"
            },
            new User
            {
                Id = otherUserId,
                RoleId = operatorRoleId,
                FullName = "Other",
                Email = "other@fixture.test"
            });

        // --------------------------------------------------------
        // Pilot
        // --------------------------------------------------------

        db.Pilots.Add(
            new Pilot
            {
                Id = pilotId,
                UserId = ownerId,
                PilotLicenseNo = "FIXTURE"
            });

        // --------------------------------------------------------
        // UAV
        // --------------------------------------------------------

        db.UAVs.Add(
            new UAV
            {
                Id = uavId,
                PilotId = pilotId,
                Code = "IMG-1",
                Name = "Image fixture",
                Status = "Active"
            });

        await db.SaveChangesAsync();

        // --------------------------------------------------------
        // Dependencies
        // --------------------------------------------------------

        var storage = new TrackingStorage();

        INotificationService notifications =
            new NotificationService(
                new NotificationRepository(db),
                new MissionRepository(db));

        var service =
            new UavService(
                new UavRepository(db),
                new PilotRepository(db),
                notifications,
                NullLogger<UavService>.Instance,
                storage,
                new UavReviewWorkflow(db));

        // ========================================================
        // UPDATE WITHOUT CHANGING OPERATIONAL STATUS
        // ========================================================

        await service.UpdateAsync(
            ownerId,
            uavId,
            new DroneOps.Application.DTOs.Request.UAVs.UpdateUavRequest
            {
                ExpectedVersion = 0,
                Code = "IMG-1",
                Name = "Image fixture"
            });

        var unchangedUav =
            await GetUavAsync(
                db,
                uavId);

        check(
            unchangedUav.Status == "Active",
            "unchanged edit preserves the UAV operational status");

        // ========================================================
        // FIRST IMAGE UPLOAD
        // ========================================================

        await using (
            var firstImage =
                new MemoryStream(PngFixture))
        {
            await service.UploadOrReplaceUavImageAsync(
                uavId,
                ownerId,
                "uav_operator",
                firstImage,
                "first.png",
                "image/png",
                PngFixture.Length);
        }

        var firstUpload =
            await GetUavAsync(
                db,
                uavId);

        var firstPath =
            firstUpload.ImageStoragePath;

        check(
            firstPath is not null &&
            storage.Paths.SetEquals([firstPath]),
            "first UAV image is stored and referenced by ImageStoragePath");

        check(
            firstUpload.Status == "Active",
            "optional image upload preserves the UAV operational status");

        // ========================================================
        // REPLACE IMAGE
        // ========================================================

        await using (
            var secondImage =
                new MemoryStream(PngFixture))
        {
            await service.UploadOrReplaceUavImageAsync(
                uavId,
                ownerId,
                "uav_operator",
                secondImage,
                "second.png",
                "image/png",
                PngFixture.Length);
        }

        var secondUpload =
            await GetUavAsync(
                db,
                uavId);

        var secondPath =
            secondUpload.ImageStoragePath;

        check(
            secondPath is not null &&
            secondPath != firstPath &&
            storage.Paths.SetEquals([secondPath]) &&
            storage.Deleted.Contains(firstPath!),
            "second UAV image replaces the first and removes the old storage object");

        check(
            secondUpload.Status == "Active",
            "replacing UAV image does not modify operational status");

        // ========================================================
        // ANOTHER PILOT CANNOT REPLACE IMAGE
        // ========================================================

        try
        {
            await using var unauthorizedImage =
                new MemoryStream(PngFixture);

            await service.UploadOrReplaceUavImageAsync(
                uavId,
                otherUserId,
                "uav_operator",
                unauthorizedImage,
                "other.png",
                "image/png",
                PngFixture.Length);

            throw new InvalidOperationException(
                "Another pilot replaced an owned UAV image.");
        }
        catch (UnauthorizedAccessException)
        {
            check(
                true,
                "another pilot cannot replace an owned UAV image");
        }

        // ========================================================
        // DELETE IMAGE
        // ========================================================

        var deleted =
            await service.DeleteUavImageAsync(
                uavId,
                ownerId,
                "uav_operator");

        var afterDelete =
            await GetUavAsync(
                db,
                uavId);

        check(
            deleted &&
            afterDelete.ImageStoragePath is null &&
            afterDelete.Status == "Active",
            "optional image deletion clears the image and preserves UAV operation");

        check(
            secondPath is not null &&
            storage.Deleted.Contains(secondPath),
            "image deletion removes the current storage object");
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private static Task<UAV> GetUavAsync(
        AppDbContext db,
        Guid uavId)
    {
        return db.UAVs
            .AsNoTracking()
            .SingleAsync(
                x => x.Id == uavId);
    }

    // ============================================================
    // STORAGE FIXTURE
    // ============================================================

    private sealed class TrackingStorage :
        IUserMediaStorage
    {
        public HashSet<string> Paths { get; } = [];

        public HashSet<string> Deleted { get; } = [];

        public Task ValidateAsync(
            IFormFile file,
            bool avatar,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UploadAsync(
            string path,
            IFormFile file,
            CancellationToken cancellationToken = default)
        {
            Paths.Add(path);

            return Task.CompletedTask;
        }

        public async Task<byte[]>
            ReadValidatedUavImageAsync(
                Stream stream,
                string fileName,
                string contentType,
                long fileSize,
                CancellationToken cancellationToken = default)
        {
            using var buffer =
                new MemoryStream();

            await stream.CopyToAsync(
                buffer,
                cancellationToken);

            return buffer.ToArray();
        }

        public Task UploadAsync(
            string path,
            Stream stream,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            Paths.Add(path);

            return Task.CompletedTask;
        }

        public Task<string> SignAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                "https://fixture.invalid/" + path);
        }

        public Task<byte[]> DownloadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                PngFixture);
        }

        public Task DeleteAsync(
            IEnumerable<string> paths,
            CancellationToken cancellationToken = default)
        {
            foreach (var path in paths)
            {
                Paths.Remove(path);

                Deleted.Add(path);
            }

            return Task.CompletedTask;
        }
    }

    // ============================================================
    // GEMINI HTTP FIXTURE
    // ============================================================

    private sealed class GeminiHandler(
        HttpStatusCode statusCode,
        string resultJson)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            var responseBody =
                string.IsNullOrWhiteSpace(resultJson)
                    ? "{}"
                    : JsonSerializer.Serialize(
                        new
                        {
                            candidates =
                                new[]
                                {
                                    new
                                    {
                                        content =
                                            new
                                            {
                                                parts =
                                                    new[]
                                                    {
                                                        new
                                                        {
                                                            text =
                                                                resultJson
                                                        }
                                                    }
                                            }
                                    }
                                }
                        });

            var response =
                new HttpResponseMessage(
                    statusCode)
                {
                    Content =
                        new StringContent(
                            responseBody,
                            Encoding.UTF8,
                            "application/json")
                };

            return Task.FromResult(
                response);
        }
    }
}