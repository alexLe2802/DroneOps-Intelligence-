using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Users;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Application.DTOs.Response.Pages;
using DroneOps.Domain.Entities;
using BCrypt.Net;

namespace DroneOps.Application.Services.Users;

public class PilotRegistrationService
    : IPilotRegistrationService
{
    private readonly IPilotRegistrationRepository _repository;

    public PilotRegistrationService(
        IPilotRegistrationRepository repository)
    {
        _repository = repository;
    }

    public async Task RegisterAsync(
        CreatePilotRegistrationRequest request)
    {
        var existed = await _repository
            .EmailExistsAsync(request.Email);

        if (existed)
        {
            throw new Exception(
                "Email already registered.");
        }

        var registration = new PilotRegistration
        {
            Id = Guid.NewGuid(),

            FullName = request.FullName,

            Email = request.Email,

            PasswordHash = BCrypt.Net.BCrypt.HashPassword(
          request.Password),

            PhoneNumber = request.PhoneNumber,

            DateOfBirth = request.DateOfBirth,

            DroneType = request.DroneType,

            DroneModel = request.DroneModel,

            PilotLicenseNo = request.PilotLicenseNo,

            LicenseIssuedDate = request.LicenseIssuedDate,

            LicenseExpiredDate = request.LicenseExpiredDate,

            ExperienceYears = request.ExperienceYears,

            UsagePurpose = request.UsagePurpose,

            Description = request.Description,

            Status = "Pending",

            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(registration);

        await _repository.SaveChangesAsync();
    }
   
    public async Task<PilotRegistrationDetailResponse?> GetDetailAsync(
    Guid id)
    {
        var registration = await _repository.GetByIdAsync(id);

        if (registration == null)
        {
            return null;
        }

        return new PilotRegistrationDetailResponse
        {
            Id = registration.Id,
            FullName = registration.FullName,
            Email = registration.Email,
            PhoneNumber = registration.PhoneNumber,
            DateOfBirth = registration.DateOfBirth,
            DroneType = registration.DroneType,
            DroneModel = registration.DroneModel,
            PilotLicenseNo = registration.PilotLicenseNo,
            LicenseIssuedDate = registration.LicenseIssuedDate,
            LicenseExpiredDate = registration.LicenseExpiredDate,
            ExperienceYears = registration.ExperienceYears,
            UsagePurpose = registration.UsagePurpose,
            Description = registration.Description,
            Status = registration.Status,
            RejectReason = registration.RejectReason,
            ReviewedBy = registration.ReviewedBy,
            ReviewedAt = registration.ReviewedAt,
            CreatedAt = registration.CreatedAt
        };
    }
    public async Task<PagedPilotRegistrationResponse> GetAllAsync(
    GetPilotRegistrationsRequest request)
    {
        var result = await _repository.GetAllAsync(
            request.PageNumber,
            request.PageSize,
            request.Search,
            request.Status);

        return new PagedPilotRegistrationResponse
        {
            Items = result.Items.Select(x => new PilotRegistrationResponse
            {
                Id = x.Id,
                FullName = x.FullName,
                Email = x.Email,
                PhoneNumber = x.PhoneNumber,
                DroneType = x.DroneType,
                Status = x.Status,
                CreatedAt = x.CreatedAt
            }).ToList(),

            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalItems = result.TotalCount,
            TotalPages = (int)Math.Ceiling(
                result.TotalCount / (double)request.PageSize)
        };
    }
}