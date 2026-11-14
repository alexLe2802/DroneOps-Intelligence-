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
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using DroneOps.Application.Services;

internal static class LicenseWorkflowChecks
{
    public static async Task RunAsync(string root)
    {
        // Never load developer credentials or mutate a shared database for these checks.
        var connection = CiDatabaseChecks.LocalConnection();
        connection.Database = $"droneops_ci_profile_{Guid.NewGuid():N}";
        await using var source = NpgsqlDataSource.Create(connection.ConnectionString);
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(source, options => options.EnableRetryOnFailure())
                .Options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX "UX_ProfileCheck_Pending" ON "PilotUpdateRequest" ("PilotId") WHERE "Status"='Pending';
            CREATE TABLE droneops.auth_sessions(id uuid PRIMARY KEY, account_id uuid NOT NULL,
                token_hash text, login_proof_hash text, expires_at timestamptz, user_agent text, revoked_at timestamptz);
            """);
        db.Roles.AddRange(new Role { Id=Guid.NewGuid(),Name="uav_operator" },new Role {Id=Guid.NewGuid(),Name="operation_manager"});
        await db.SaveChangesAsync();
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
                PhoneNumber = "0901234567", DateOfBirth = today.AddYears(-25), ExperienceYears = 4, ExpectedVersion = 2
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
                try{await new AvatarService(users,new FailingStorage(),NullLogger<AvatarService>.Instance).UploadAvatarAsync(userId,avatarFile,expectedVersion:3);throw new Exception("FAIL: failed avatar upload accepted");}
                catch(HttpRequestException){db.ChangeTracker.Clear();Check((await db.Users.AsNoTracking().SingleAsync(x=>x.Id==userId)).AvatarPath==oldAvatar,"failed avatar upload preserves the current private avatar");}
            }

            await using (var staleContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options))
            {
                var stalePilots = new PilotRepository(staleContext);
                await stalePilots.GetByUserIdForUpdateAsync(userId);
                await service.UpdateProfileAsync(userId,new UpdateProfileRequest {FullName="Concurrent name",ExpectedVersion=3});
                var staleService = new UserService(new UserRepository(staleContext),stalePilots,mail,cache,staleContext);
                try { await staleService.UpdatePilotProfileAsync(userId,new UpdatePilotProfileRequest {PhoneNumber="0909999999",ExpectedVersion=3}); throw new Exception("FAIL: cross-form stale update accepted"); }
                catch(DbUpdateConcurrencyException) { Check(true,"separate contexts reject a stale operator edit after a name change"); }
            }
            db.ChangeTracker.Clear();
            Check((await db.Pilots.AsNoTracking().SingleAsync(x=>x.Id==pilotId)).PhoneNumber=="0901234567",
                "profile conflict rolls back the operator fields atomically");
            await using(var avatarStream=new MemoryStream([1,2,3]))
            {
                var avatarFile=new FormFile(avatarStream,0,3,"file","avatar.png"){Headers=new HeaderDictionary(),ContentType="image/png"};
                var storage=new TestStorage();
                try { await new AvatarService(users,storage,NullLogger<AvatarService>.Instance).UploadAvatarAsync(userId,avatarFile,expectedVersion:3); throw new Exception("FAIL: stale avatar accepted"); }
                catch(DbUpdateConcurrencyException) { Check(storage.UploadCount==0,"stale avatar is rejected before uploading a file"); }
                storage.OnUpload=async()=>
                {
                    await using var other=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options);
                    var current=await other.Users.SingleAsync(x=>x.Id==userId);
                    current.FullName="Changed during upload";current.ProfileVersion++;
                    await other.SaveChangesAsync();
                };
                try { await new AvatarService(users,storage,NullLogger<AvatarService>.Instance).UploadAvatarAsync(userId,avatarFile,expectedVersion:4); throw new Exception("FAIL: avatar race accepted"); }
                catch(DbUpdateConcurrencyException)
                {
                    db.ChangeTracker.Clear();
                    Check(storage.Deleted.Count==1&&storage.Deleted[0]!=oldAvatar
                        &&(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==userId)).AvatarPath==oldAvatar,
                        "avatar race cleans only the new upload and preserves the saved avatar");
                }
            }

            var originalEmail=(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==userId)).Email;
            var raceEmail=$"race-{userId:N}@example.com";
            await service.RequestChangeEmailAsync(userId,new ChangeEmailRequest {NewEmail=raceEmail,CurrentPassword="Test-only-123!"});
            var raceCode=Regex.Match(mail.Html,@">([0-9]{6})</span>").Groups[1].Value;
            Check(raceCode.Length==6,"email challenge contains exactly six numeric digits");
            try { await service.RequestChangeEmailAsync(userId,new ChangeEmailRequest {NewEmail=raceEmail,CurrentPassword="Test-only-123!"}); throw new Exception("FAIL: resend cooldown bypassed"); }
            catch(InvalidOperationException) { Check(true,"email challenge resend enforces the 60-second cooldown"); }
            var scopedRejected=false;try{await service.ConfirmChangeEmailAsync(userId,new VerifyChangeEmailRequest {NewEmail=$"other-{userId:N}@example.com",Otp=raceCode});}
            catch(Exception){scopedRejected=true;}Check(scopedRejected&&(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==userId)).Email==originalEmail,"challenge is scoped to account and exact new email");
            var managerOriginal=(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==managerId)).Email;
            await db.Users.Where(x=>x.Id==managerId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Email,raceEmail));
            var duplicateRejected=false;try{await service.ConfirmChangeEmailAsync(userId,new VerifyChangeEmailRequest {NewEmail=raceEmail,Otp=raceCode});}
            catch(Exception){duplicateRejected=true;}Check(duplicateRejected&&(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==userId)).Email==originalEmail,"atomic uniqueness recheck preserves current email on confirmation conflict");
            await db.Users.Where(x=>x.Id==managerId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Email,managerOriginal));

            var changedEmail = $"verified-{userId:N}@example.com";
            await db.EmailChangeChallenges.Where(x=>x.AccountId==userId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,DateTimeOffset.UtcNow.AddMinutes(-2)));
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO droneops.auth_sessions(id,account_id,token_hash,login_proof_hash,expires_at,user_agent) VALUES ({Guid.NewGuid()},{userId},{new string('a',64)},{new string('b',64)},now()+interval '1 day','fixture')");
            await service.RequestChangeEmailAsync(userId, new ChangeEmailRequest
            {
                NewEmail = changedEmail, CurrentPassword = "Test-only-123!"
            });
            var code = Regex.Match(mail.Html, @">([0-9]{6})</span>").Groups[1].Value;
            try { await service.ConfirmChangeEmailAsync(userId,new VerifyChangeEmailRequest {NewEmail=raceEmail,Otp=raceCode}); throw new Exception("FAIL: superseded OTP accepted"); }
            catch(Exception e) when(!e.Message.StartsWith("FAIL:")) { Check(true,"resend invalidates the previous email challenge"); }
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
            try { await service.ConfirmChangeEmailAsync(userId,new VerifyChangeEmailRequest {NewEmail=changedEmail,Otp=code}); throw new Exception("FAIL: consumed OTP replayed"); }
            catch(Exception e) when(!e.Message.StartsWith("FAIL:")) { Check(true,"consumed OTP cannot be replayed"); }

            await db.EmailChangeChallenges.Where(x=>x.AccountId==userId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,DateTimeOffset.UtcNow.AddMinutes(-2)));
            var nextEmail=$"next-{userId:N}@example.com";
            async Task<bool> ConcurrentEmailRequest()
            {
                await using var context=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source,o=>o.EnableRetryOnFailure()).Options);
                using var localCache=new MemoryCache(new MemoryCacheOptions());
                var local=new UserService(new UserRepository(context),new PilotRepository(context),mail,localCache,context);
                try { await local.RequestChangeEmailAsync(userId,new ChangeEmailRequest {NewEmail=nextEmail,CurrentPassword="Test-only-123!"}); return true; }
                catch(InvalidOperationException) { return false; }
            }
            var emails=await Task.WhenAll(ConcurrentEmailRequest(),ConcurrentEmailRequest());
            Check(emails.Count(x=>x)==1&&await db.EmailChangeChallenges.CountAsync(x=>x.AccountId==userId&&x.Status=="Pending")==1,
                "concurrent resends create one pending challenge and enforce cooldown");
            var nextCode=Regex.Match(mail.Html,@">([0-9]{6})</span>").Groups[1].Value;
            var wrong=nextCode=="000000"?"111111":"000000";
            for(var attempt=0;attempt<5;attempt++)
            {
                try { await service.ConfirmChangeEmailAsync(userId,new VerifyChangeEmailRequest {NewEmail=nextEmail,Otp=wrong}); throw new Exception("FAIL: wrong OTP accepted"); }
                catch(Exception e) when(!e.Message.StartsWith("FAIL:")) { }
            }
            Check(await db.EmailChangeChallenges.AnyAsync(x=>x.AccountId==userId&&x.Status=="Locked"&&x.FailedAttempts==5),
                "five incorrect attempts invalidate the email challenge");
            try { await service.ConfirmChangeEmailAsync(userId,new VerifyChangeEmailRequest {NewEmail=nextEmail,Otp=nextCode}); throw new Exception("FAIL: locked OTP accepted"); }
            catch(Exception e) when(!e.Message.StartsWith("FAIL:")) { Check(true,"correct OTP cannot bypass a locked challenge"); }
            await db.EmailChangeChallenges.Where(x=>x.AccountId==userId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,DateTimeOffset.UtcNow.AddMinutes(-2)));
            await service.RequestChangeEmailAsync(userId,new ChangeEmailRequest {NewEmail=nextEmail,CurrentPassword="Test-only-123!"});
            nextCode=Regex.Match(mail.Html,@">([0-9]{6})</span>").Groups[1].Value;
            await db.EmailChangeChallenges.Where(x=>x.AccountId==userId&&x.Status=="Pending").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ExpiresAt,DateTimeOffset.UtcNow.AddSeconds(-1)));
            try { await service.ConfirmChangeEmailAsync(userId,new VerifyChangeEmailRequest {NewEmail=nextEmail,Otp=nextCode}); throw new Exception("FAIL: expired OTP accepted"); }
            catch(Exception e) when(!e.Message.StartsWith("FAIL:")) { Check(await db.EmailChangeChallenges.AnyAsync(x=>x.AccountId==userId&&x.Status=="Expired"),"expired OTP leaves the current email unchanged"); }

            await service.UpdatePilotProfileAsync(userId, LicenseRequest("UPDATED-1"));
            db.ChangeTracker.Clear();
            var request = await db.PilotUpdateRequests.AsNoTracking().SingleAsync(item => item.PilotId == pilotId);
            Check(request.Status == "Pending", "license update remains pending until review");

            var replacement=LicenseRequest("UPDATED-1B");replacement.ExpectedPendingRequestId=request.Id;
            await service.UpdatePilotProfileAsync(userId,replacement);db.ChangeTracker.Clear();
            var pending=await db.PilotUpdateRequests.AsNoTracking().SingleAsync(x=>x.PilotId==pilotId&&x.Status=="Pending");
            Check(await db.PilotUpdateRequests.AnyAsync(x=>x.Id==request.Id&&x.Status=="Superseded")&&pending.PilotLicenseNo=="UPDATED-1B",
                "a new proposal supersedes history while retaining exactly one pending request");
            Check((await db.Pilots.AsNoTracking().SingleAsync(x=>x.Id==pilotId)).PilotLicenseNo=="ORIGINAL",
                "pending replacement preserves the current approved license");
            try { await service.UpdatePilotProfileAsync(userId,replacement); throw new Exception("FAIL: stale replacement accepted"); }
            catch(InvalidOperationException) { Check(true,"stale replacement cannot supersede a newer pending request"); }
            try { await service.ApprovePilotUpdateRequestAsync(request.Id,managerId); throw new Exception("FAIL: superseded approval accepted"); }
            catch(InvalidOperationException) { Check(true,"manager cannot approve superseded license evidence"); }
            var expectedPending=pending.Id;
            async Task<bool> ConcurrentReplace()
            {
                await using var context=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source,o=>o.EnableRetryOnFailure()).Options);
                using var localCache=new MemoryCache(new MemoryCacheOptions());
                var local=new UserService(new UserRepository(context),new PilotRepository(context),mail,localCache,context);
                var proposal=LicenseRequest("UPDATED-1B");proposal.ExpectedPendingRequestId=expectedPending;
                try { await local.UpdatePilotProfileAsync(userId,proposal); return true; }
                catch(InvalidOperationException) { return false; }
                catch(DbUpdateConcurrencyException) { return false; }
            }
            await using(var staleReview=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options))
            {
                var staleRepository=new PilotRepository(staleReview);
                await staleRepository.GetUpdateRequestForReviewAsync(expectedPending);
                var replacements=await Task.WhenAll(ConcurrentReplace(),ConcurrentReplace());
                Check(replacements.Count(x=>x)==1&&await db.PilotUpdateRequests.CountAsync(x=>x.PilotId==pilotId&&x.Status=="Pending")==1,
                    "concurrent replacements accept exactly one successor");
                var staleService=new UserService(new UserRepository(staleReview),staleRepository,mail,cache,staleReview);
                try { await staleService.ApprovePilotUpdateRequestAsync(expectedPending,managerId); throw new Exception("FAIL: preloaded stale review accepted"); }
                catch(DbUpdateConcurrencyException) { Check(true,"preloaded manager decision conflicts after pending replacement"); }
            }
            db.ChangeTracker.Clear();
            pending=await db.PilotUpdateRequests.AsNoTracking().SingleAsync(x=>x.PilotId==pilotId&&x.Status=="Pending");

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
                catch(DbUpdateConcurrencyException){return false;}
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
            await db.Database.EnsureDeletedAsync();
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
        public int UploadCount {get;private set;}
        public Func<Task>? OnUpload {get;set;}
        public List<string> Deleted {get;} = [];
        public Task ValidateAsync(IFormFile file, bool avatar, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public async Task UploadAsync(string path, IFormFile file, CancellationToken cancellationToken = default)
        { UploadCount++; if(OnUpload is not null) await OnUpload(); }
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
        public Task<byte[]> DownloadAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<byte>());
        public Task DeleteAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        { Deleted.AddRange(paths); return Task.CompletedTask; }
    }

    private sealed class FailingStorage : IUserMediaStorage
    {
        public Task ValidateAsync(IFormFile file,bool avatar,CancellationToken ct=default)=>Task.CompletedTask;
        public Task UploadAsync(string path,IFormFile file,CancellationToken ct=default)=>throw new HttpRequestException("fixture");
        public Task UploadAsync(string path,Stream stream,string contentType,CancellationToken ct=default)=>throw new HttpRequestException("fixture");
        public Task<byte[]> ReadValidatedUavImageAsync(Stream stream,string fileName,string contentType,long fileSize,CancellationToken ct=default)=>Task.FromResult(Array.Empty<byte>());
        public Task<string> SignAsync(string path,CancellationToken ct=default)=>Task.FromResult("https://test.invalid");
        public Task<byte[]> DownloadAsync(string path,CancellationToken ct=default)=>throw new HttpRequestException("fixture");
        public Task DeleteAsync(IEnumerable<string> paths,CancellationToken ct=default)=>Task.CompletedTask;
    }
}
