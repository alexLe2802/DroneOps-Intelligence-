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
using Microsoft.Extensions.Logging.Abstractions;
using DroneOps.Application.Services;

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
        var service = new UserService(users, pilots, mail, cache,db);
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

            var profile=await service.UpdateProfileAsync(userId,new UpdateProfileRequest {FullName="Versioned Profile",ExpectedVersion=1});
            Check(profile?.ProfileVersion==2,"profile update advances its optimistic concurrency version");
            try{await service.UpdateProfileAsync(userId,new UpdateProfileRequest {FullName="Stale",ExpectedVersion=1});throw new Exception("FAIL: stale profile update accepted");}
            catch(DbUpdateConcurrencyException){Check(true,"stale profile update returns a concurrency conflict");}

            await service.UpdatePilotProfileAsync(userId, new UpdatePilotProfileRequest
            {
                PhoneNumber = "0901234567", DateOfBirth = today.AddYears(-25), ExperienceYears = 4
            });
            db.ChangeTracker.Clear();
            var pilot = await db.Pilots.AsNoTracking().SingleAsync(item => item.Id == pilotId);
            Check(pilot.PhoneNumber == "0901234567" && pilot.ExperienceYears == 4,
                "pilot profile persisted");

            var oldAvatar=$"avatars/{userId:N}/{Guid.NewGuid():N}";
            await db.Users.Where(x=>x.Id==userId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AvatarPath,oldAvatar));
            await using(var avatarStream=new MemoryStream([1,2,3]))
            {
                var avatarFile=new FormFile(avatarStream,0,3,"file","avatar.png"){Headers=new HeaderDictionary(),ContentType="image/png"};
                try{await new AvatarService(users,new FailingStorage(),NullLogger<AvatarService>.Instance).UploadAvatarAsync(userId,avatarFile);throw new Exception("FAIL: failed avatar upload accepted");}
                catch(HttpRequestException){db.ChangeTracker.Clear();Check((await db.Users.AsNoTracking().SingleAsync(x=>x.Id==userId)).AvatarPath==oldAvatar,"failed avatar upload preserves the current private avatar");}
            }

            var originalEmail=(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==userId)).Email;
            var raceEmail=$"race-{userId:N}@example.com";
            await service.RequestChangeEmailAsync(userId,new ChangeEmailRequest {NewEmail=raceEmail,CurrentPassword="Test-only-123!"});
            var raceCode=Regex.Match(mail.Html,@">([A-Z0-9]{5})</span>").Groups[1].Value;
            var scopedRejected=false;try{await service.ConfirmChangeEmailAsync(userId,new VerifyChangeEmailRequest {NewEmail=$"other-{userId:N}@example.com",Otp=raceCode});}
            catch(Exception){scopedRejected=true;}Check(scopedRejected&&(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==userId)).Email==originalEmail,"challenge is scoped to account and exact new email");
            var managerOriginal=(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==managerId)).Email;
            await db.Users.Where(x=>x.Id==managerId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Email,raceEmail));
            var duplicateRejected=false;try{await service.ConfirmChangeEmailAsync(userId,new VerifyChangeEmailRequest {NewEmail=raceEmail,Otp=raceCode});}
            catch(Exception){duplicateRejected=true;}Check(duplicateRejected&&(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==userId)).Email==originalEmail,"atomic uniqueness recheck preserves current email on confirmation conflict");
            await db.Users.Where(x=>x.Id==managerId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Email,managerOriginal));

            var changedEmail = $"verified-{userId:N}@example.com";
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO droneops.auth_sessions(id,account_id,token_hash,login_proof_hash,expires_at,user_agent) VALUES ({Guid.NewGuid()},{userId},{new string('a',64)},{new string('b',64)},now()+interval '1 day','fixture')");
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
            Check(!await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM droneops.auth_sessions WHERE account_id={userId} AND revoked_at IS NULL").AnyAsync()
                  && await db.AccountWorkflowAudits.AnyAsync(x=>x.TargetId==userId&&x.Action=="email.changed"&&x.Result=="Succeeded"),
                "email confirmation atomically revokes sessions and records actor/target/result/time audit");

            await service.UpdatePilotProfileAsync(userId, LicenseRequest("UPDATED-1"));
            db.ChangeTracker.Clear();
            var request = await db.PilotUpdateRequests.AsNoTracking().SingleAsync(item => item.PilotId == pilotId);
            Check(request.Status == "Pending", "license update remains pending until review");

            await service.UpdatePilotProfileAsync(userId,LicenseRequest("UPDATED-1B"));db.ChangeTracker.Clear();
            var pending=await db.PilotUpdateRequests.AsNoTracking().SingleAsync(x=>x.PilotId==pilotId&&x.Status=="Pending");
            Check(await db.PilotUpdateRequests.AnyAsync(x=>x.Id==request.Id&&x.Status=="Superseded")&&pending.PilotLicenseNo=="UPDATED-1B",
                "a new proposal supersedes history while retaining exactly one pending request");

            try{await service.ApprovePilotUpdateRequestAsync(pending.Id,userId);throw new Exception("FAIL: self review accepted");}
            catch(InvalidOperationException){Check(true,"license request author cannot review their own request");}
            await service.ApprovePilotUpdateRequestAsync(pending.Id, managerId);
            db.ChangeTracker.Clear();
            var credentials = await credentialsService.GetCredentialsAsync(userId);
            Check(credentials?.PilotLicenseNo == "UPDATED-1B" && credentials.LicenseImageUrls.Count == 1,
                "approved credentials and evidence are available");

            await service.UpdatePilotProfileAsync(userId, LicenseRequest("UPDATED-2"));
            db.ChangeTracker.Clear();
            var second = await db.PilotUpdateRequests.AsNoTracking()
                .SingleAsync(item => item.PilotId == pilotId && item.Status == "Pending");
            await service.RejectPilotUpdateRequestAsync(
                second.Id, managerId, new RejectPilotUpdateRequest { RejectReason = "Test rejection" });
            db.ChangeTracker.Clear();
            Check((await db.Pilots.AsNoTracking().SingleAsync(item => item.Id == pilotId)).PilotLicenseNo == "UPDATED-1B",
                "rejection preserves approved license");
            Check(await db.PilotUpdateDecisionAudits.CountAsync(x=>x.TargetUserId==userId)==2,
                "immutable decision audits retain actor, target, time and result");

            await service.UpdatePilotProfileAsync(userId,LicenseRequest("CONCURRENT"));db.ChangeTracker.Clear();
            var concurrent=await db.PilotUpdateRequests.AsNoTracking().SingleAsync(x=>x.PilotId==pilotId&&x.Status=="Pending");
            async Task<bool> ConcurrentApprove()
            {
                await using var context=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source,o=>o.EnableRetryOnFailure()).Options);
                using var localCache=new MemoryCache(new MemoryCacheOptions());
                var local=new UserService(new UserRepository(context),new PilotRepository(context),mail,localCache,context);
                try{await local.ApprovePilotUpdateRequestAsync(concurrent.Id,managerId);return true;}
                catch(InvalidOperationException){return false;}
            }
            var decisions=await Task.WhenAll(ConcurrentApprove(),ConcurrentApprove());db.ChangeTracker.Clear();
            Check(decisions.Count(x=>x)==1&&await db.PilotUpdateDecisionAudits.CountAsync(x=>x.RequestId==concurrent.Id)==1,
                "concurrent decision creates one result and one conflict without duplicate audit");

            Console.WriteLine($"{count} workflow integration checks passed. No email or file was sent.");
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.AccountWorkflowAudits.Where(x=>x.TargetId==userId||x.ActorId==userId).ExecuteDeleteAsync();
            await db.EmailChangeChallenges.Where(x=>x.AccountId==userId).ExecuteDeleteAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM droneops.auth_sessions WHERE account_id={userId}");
            await db.PilotUpdateDecisionAudits.Where(x=>x.TargetUserId==userId).ExecuteDeleteAsync();
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

    private sealed class FailingStorage : IUserMediaStorage
    {
        public Task ValidateAsync(IFormFile file,bool avatar,CancellationToken ct=default)=>Task.CompletedTask;
        public Task UploadAsync(string path,IFormFile file,CancellationToken ct=default)=>throw new HttpRequestException("fixture");
        public Task UploadAsync(string path,Stream stream,string contentType,CancellationToken ct=default)=>throw new HttpRequestException("fixture");
        public Task<byte[]> ReadValidatedUavImageAsync(Stream stream,string fileName,string contentType,long fileSize,CancellationToken ct=default)=>Task.FromResult(Array.Empty<byte>());
        public Task<string> SignAsync(string path,CancellationToken ct=default)=>Task.FromResult("https://test.invalid");
        public Task DeleteAsync(IEnumerable<string> paths,CancellationToken ct=default)=>Task.CompletedTask;
    }
}
