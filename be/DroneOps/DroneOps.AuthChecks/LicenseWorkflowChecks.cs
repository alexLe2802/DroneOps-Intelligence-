using System.Text.RegularExpressions;
using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.Interfaces.Auth;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Application.Services.Users;
using DroneOps.Domain.Entities;
using DroneOps.Persistence;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

internal static class LicenseWorkflowChecks
{
    public static async Task RunAsync(string root)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(root + "/be/DroneOps/DroneOps.API/appsettings.Local.json")
            .Build();
        await using var source = PostgresDataSourceFactory.Create(
            config.GetConnectionString("DefaultConnection")!);
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(source, options => options.EnableRetryOnFailure())
                .Options);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var mail = new TestMail();
        var users = new UserRepository(db);
        var pilots = new PilotRepository(db);
        var service = new UserService(users, pilots, mail, cache);
        var credentialsService = new OperatorCredentialsService(users, pilots, new TestStorage());
        var userId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var pilotId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var count = 0;
        void Check(bool ok,string name) { if(!ok) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); count++; }
        UpdatePilotProfileRequest LicenseRequest(string license) => new()
        {
            PilotLicenseNo = license,
            LicenseIssuedDate = today.AddDays(-1),
            LicenseExpiredDate = today.AddYears(2),
            LicenseImageUrls = [$"licenses/{userId:N}/{license}.png"]
        };

        try
        {
            var roles = await db.Roles.AsNoTracking().ToListAsync();
            var operatorRole = roles.First(role => role.Name == "uav_operator");
            var managerRole = roles.First(role => role.Name == "operation_manager");
            db.Users.AddRange(
                new User
                {
                    Id = userId, RoleId = operatorRole.Id,
                    Email = $"workflow-{userId:N}@example.com", FullName = "Temporary Workflow Check",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test-only-123!"), CreatedAt = DateTimeOffset.UtcNow
                },
                new User
                {
                    Id = managerId, RoleId = managerRole.Id,
                    Email = $"workflow-{managerId:N}@example.com", FullName = "Temporary Workflow Manager",
                    PasswordHash = "unused", CreatedAt = DateTimeOffset.UtcNow
                });
            db.Pilots.Add(new Pilot
            {
                Id = pilotId, UserId = userId, PilotLicenseNo = "ORIGINAL",
                LicenseIssuedDate = today.AddYears(-1), LicenseExpiredDate = today.AddYears(1),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await service.UpdatePilotProfileAsync(userId, new UpdatePilotProfileRequest
            {
                PhoneNumber = "0901234567", DateOfBirth = today.AddYears(-25), ExperienceYears = 4
            });
            db.ChangeTracker.Clear();
            var pilot = await db.Pilots.AsNoTracking().SingleAsync(item => item.Id == pilotId);
            Check(pilot.PhoneNumber == "0901234567" && pilot.ExperienceYears == 4,
                "pilot profile persisted");

            var changedEmail = $"verified-{userId:N}@example.com";
            await service.RequestChangeEmailAsync(userId, new ChangeEmailRequest
            {
                NewEmail = changedEmail, CurrentPassword = "Test-only-123!"
            });
            var code = Regex.Match(mail.Html, @">([A-Z0-9]{5})</span>").Groups[1].Value;
            await service.ConfirmChangeEmailAsync(userId, new VerifyChangeEmailRequest
            {
                NewEmail = changedEmail, Otp = code
            });
            db.ChangeTracker.Clear();
            Check(mail.To == changedEmail &&
                  (await db.Users.AsNoTracking().SingleAsync(item => item.Id == userId)).Email == changedEmail,
                "email updated only after verification");

            await service.UpdatePilotProfileAsync(userId, LicenseRequest("UPDATED-1"));
            db.ChangeTracker.Clear();
            var request = await db.PilotUpdateRequests.AsNoTracking().SingleAsync(item => item.PilotId == pilotId);
            Check(request.Status == "Pending", "license update remains pending until review");

            try
            {
                await service.UpdatePilotProfileAsync(userId, LicenseRequest("DUPLICATE"));
                throw new Exception("FAIL: duplicate pending request accepted");
            }
            catch (InvalidOperationException)
            {
                Check(true, "second pending request rejected");
            }

            await service.ApprovePilotUpdateRequestAsync(request.Id, managerId);
            db.ChangeTracker.Clear();
            var credentials = await credentialsService.GetCredentialsAsync(userId);
            Check(credentials?.PilotLicenseNo == "UPDATED-1" && credentials.LicenseImageUrls.Count == 1,
                "approved credentials and evidence are available");

            await service.UpdatePilotProfileAsync(userId, LicenseRequest("UPDATED-2"));
            db.ChangeTracker.Clear();
            var second = await db.PilotUpdateRequests.AsNoTracking()
                .SingleAsync(item => item.PilotId == pilotId && item.Status == "Pending");
            await service.RejectPilotUpdateRequestAsync(
                second.Id, managerId, new RejectPilotUpdateRequest { RejectReason = "Test rejection" });
            db.ChangeTracker.Clear();
            Check((await db.Pilots.AsNoTracking().SingleAsync(item => item.Id == pilotId)).PilotLicenseNo == "UPDATED-1",
                "rejection preserves approved license");

            Console.WriteLine($"{count} workflow integration checks passed. No email or file was sent.");
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.LicenseImages
                .Where(item => item.PilotUpdateRequest != null && item.PilotUpdateRequest.PilotId == pilotId)
                .ExecuteDeleteAsync();
            await db.PilotUpdateRequests.Where(item => item.PilotId == pilotId).ExecuteDeleteAsync();
            await db.Pilots.Where(item => item.Id == pilotId).ExecuteDeleteAsync();
            await db.Users.Where(item => item.Id == userId || item.Id == managerId).ExecuteDeleteAsync();
            Console.WriteLine("Temporary test users, pilot, requests and files removed.");
        }
    }

    private sealed class TestMail : IEmailService
    {
        public string Html { get; private set; } = string.Empty;
        public string To { get; private set; } = string.Empty;

        public Task SendEmailAsync(string toEmail, string subject, string htmlMessage)
        {
            To = toEmail;
            Html = htmlMessage;
            return Task.CompletedTask;
        }
    }

    private sealed class TestStorage : IUserMediaStorage
    {
        public Task ValidateAsync(IFormFile file, bool avatar, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task UploadAsync(string path, IFormFile file, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public async Task<byte[]> ReadValidatedUavImageAsync(Stream stream, string fileName, string contentType, long fileSize, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        public Task UploadAsync(string path, Stream stream, string contentType, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<string> SignAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult("https://test.invalid/" + path);
        public Task DeleteAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
