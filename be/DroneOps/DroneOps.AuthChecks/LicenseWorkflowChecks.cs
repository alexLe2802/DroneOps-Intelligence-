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
        PilotUpdateRequestsController Controller(Guid userId, bool manager = false) {
         var c = new PilotUpdateRequestsController(db, service, storage, new UpdatePilotProfileValidator(), NullLogger<PilotUpdateRequestsController>.Instance);
         c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier,userId.ToString()), new Claim(ClaimTypes.Role, manager ? "operation_manager" : "uav_operator") },"test")) } };
         return c;
        }

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
        string Data(string license) => JsonSerializer.Serialize(new { pilotLicenseNo=license, licenseIssuedDate=today.AddDays(-1), licenseExpiredDate=today.AddYears(2) });
        try {
         var roles = await db.Roles.AsNoTracking().ToListAsync();
         var op = roles.First(r => r.Name == "uav_operator"); var managerRole = roles.First(r => r.Name == "operation_manager");
         db.Users.AddRange(new User {Id=uid,RoleId=op.Id,Email=$"workflow-{uid:N}@example.com",FullName="Temporary Workflow Check",PasswordHash=BCrypt.Net.BCrypt.HashPassword("Test-only-123!"),CreatedAt=DateTimeOffset.UtcNow},
         new User {Id=mid,RoleId=managerRole.Id,Email=$"workflow-{mid:N}@example.com",FullName="Temporary Workflow Manager",PasswordHash="unused",CreatedAt=DateTimeOffset.UtcNow});
         db.Pilots.Add(new Pilot {Id=pid,UserId=uid,PilotLicenseNo="ORIGINAL",LicenseIssuedDate=today.AddYears(-1),LicenseExpiredDate=today.AddYears(1),CreatedAt=DateTime.UtcNow});
         await db.SaveChangesAsync(); db.ChangeTracker.Clear();
         await service.UpdatePilotProfileAsync(uid,new UpdatePilotProfileRequest {PhoneNumber="0901234567",DateOfBirth=today.AddYears(-25),ExperienceYears=4});
         db.ChangeTracker.Clear(); var pilot = await db.Pilots.AsNoTracking().SingleAsync(p=>p.Id==pid);
         Check(pilot.PhoneNumber=="0901234567" && pilot.ExperienceYears==4 && pilot.DateOfBirth==today.AddYears(-25),"phone, birth date and flight experience persisted");
         var changedEmail=$"verified-{uid:N}@example.com";
         await service.RequestChangeEmailAsync(uid,new ChangeEmailRequest {NewEmail=changedEmail,CurrentPassword="Test-only-123!"});
         Check(mail.To==changedEmail,"verification email sent to requested address (captured test transport)");
         db.ChangeTracker.Clear(); Check((await db.Users.AsNoTracking().SingleAsync(u=>u.Id==uid)).Email!=changedEmail,"email unchanged before verification");
         var code=Regex.Match(mail.Html,@">([A-Z0-9]{5})</span>").Groups[1].Value;
         Check(code.Length==5,"verification code generated");
         try { await service.ConfirmChangeEmailAsync(uid,new VerifyChangeEmailRequest {NewEmail=changedEmail,Otp="WRONG"}); throw new Exception("bad OTP accepted"); } catch(Exception e) when(e.Message.Contains("Invalid verification")) { Check(true,"incorrect verification code rejected"); }
         await service.ConfirmChangeEmailAsync(uid,new VerifyChangeEmailRequest {NewEmail=changedEmail,Otp=code});
         db.ChangeTracker.Clear(); Check((await db.Users.AsNoTracking().SingleAsync(u=>u.Id==uid)).Email==changedEmail,"email updated only after correct OTP");
         var submitted=await Controller(uid).Submit(Data("UPDATED-1"),Files(),default);
         Check(submitted is OkObjectResult,"multipart license change uploaded and submitted"); db.ChangeTracker.Clear();
         var request=await db.PilotUpdateRequests.AsNoTracking().SingleAsync(x=>x.PilotId==pid);
         Check(request.Status=="Pending" && (await db.Pilots.AsNoTracking().SingleAsync(x=>x.Id==pid)).PilotLicenseNo=="ORIGINAL","pending request preserves official license");
         Check(await Controller(uid).Submit(Data("DUPLICATE"),Files(),default) is ConflictObjectResult,"second pending request rejected"); db.ChangeTracker.Clear();
         JsonElement Json(IActionResult value) => JsonSerializer.SerializeToElement(((OkObjectResult)value).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
         var ownList = Json(await Controller(uid).List(new() { PageSize = 100 }, default));
         Check(ownList.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == request.Id), "operator list contains own request");
         var otherList = Json(await Controller(mid).List(new(), default));
         Check(otherList.GetProperty("totalItems").GetInt32() == 0, "operator list excludes other users' requests");
         var managerList = Json(await Controller(mid,true).List(new() { Search = "Temporary Workflow Check", Status = "Pending" }, default));
         Check(managerList.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == request.Id), "manager can filter pending requests across operators");
         var ownDetail = Json(await Controller(uid).Detail(request.Id, default));
         Check(ownDetail.GetProperty("licenseImageUrls").GetArrayLength() == 1, "operator can view signed evidence for own request");
         Check(await Controller(mid).Detail(request.Id,default) is NotFoundResult,"other operator cannot open private license evidence");
         Check(await Controller(mid,true).Detail(request.Id,default) is OkObjectResult,"manager can obtain signed evidence links");
         Check(await Controller(mid,true).Approve(request.Id,default) is OkObjectResult,"manager approves request"); db.ChangeTracker.Clear();
         Check((await db.Pilots.AsNoTracking().SingleAsync(x=>x.Id==pid)).PilotLicenseNo=="UPDATED-1" && await db.LicenseImages.AnyAsync(x=>x.PilotId==pid),"approval atomically updates license and associates evidence");
         Check(await Controller(mid,true).Reject(request.Id,new() { RejectReason = "late review" },default) is ConflictObjectResult,"already reviewed request cannot be changed"); db.ChangeTracker.Clear();
         Check(await Controller(uid).Submit(Data("UPDATED-2"),Files(),default) is OkObjectResult,"new request allowed after previous review"); db.ChangeTracker.Clear();
         var second=await db.PilotUpdateRequests.AsNoTracking().SingleAsync(x=>x.PilotId==pid && x.Status=="Pending");
         Check(await Controller(mid,true).Reject(second.Id,new() { RejectReason = "" },default) is BadRequestObjectResult,"rejection requires reason");
         Check(await Controller(mid,true).Reject(second.Id,new() { RejectReason = "Test rejection" },default) is OkObjectResult,"manager rejects with reason"); db.ChangeTracker.Clear();
         Check((await db.Pilots.AsNoTracking().SingleAsync(x=>x.Id==pid)).PilotLicenseNo=="UPDATED-1","rejection preserves previously approved license");
        var credentialResult = await new OperatorCredentialsController(db, storage).Credentials(uid, default);
        var credentials = JsonSerializer.SerializeToElement(((OkObjectResult)credentialResult).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Check(credentials.GetProperty("pilotLicenseNo").GetString() == "UPDATED-1"
            && credentials.GetProperty("experienceYears").GetInt32() == 4
            && credentials.GetProperty("licenseImageUrls").GetArrayLength() == 1,
            "manager roster receives current approved credentials and signed document links");
         Check(await Controller(mid,true).Approve(Guid.NewGuid(),default) is NotFoundResult, "unknown review request returns not found");
         Check(await Controller(uid).Submit(Data("EXPIRED"),Files(),default) is OkObjectResult, "additional request created for expired-license check");
         db.ChangeTracker.Clear();
         var expired = await db.PilotUpdateRequests.SingleAsync(x => x.PilotId == pid && x.Status == "Pending");
         expired.LicenseExpiredDate = today.AddDays(-1);
         await db.SaveChangesAsync(); db.ChangeTracker.Clear();
         Check(await Controller(mid,true).Approve(expired.Id,default) is BadRequestObjectResult, "expired license cannot be approved");
         db.ChangeTracker.Clear();
         Check((await db.PilotUpdateRequests.AsNoTracking().SingleAsync(x => x.Id == expired.Id)).Status == "Pending", "failed approval leaves request pending");
         Console.WriteLine($"{count} workflow integration checks passed. SMTP delivery was simulated; no email was sent.");
        } finally {
         db.ChangeTracker.Clear();
         var paths=await db.LicenseImages.Where(x=>x.PilotUpdateRequest!=null && x.PilotUpdateRequest.PilotId==pid).Select(x=>x.ImageUrl).ToArrayAsync();
         await storage.DeleteAsync(paths,default);
         await db.LicenseImages.Where(x=>x.PilotUpdateRequest!=null && x.PilotUpdateRequest.PilotId==pid).ExecuteDeleteAsync();
         await db.PilotUpdateRequests.Where(x=>x.PilotId==pid).ExecuteDeleteAsync();
         await db.Pilots.Where(x=>x.Id==pid).ExecuteDeleteAsync();
         await db.Users.Where(x=>x.Id==uid || x.Id==mid).ExecuteDeleteAsync();
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
        public Task<string> SignAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult("https://test.invalid/" + path);
        public Task DeleteAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
