using DroneOps.Application.DTOs.Request.Users;
using FluentValidation;

namespace DroneOps.Application.Validators.Users;

public class CreatePilotRegistrationValidator
    : AbstractValidator<CreatePilotRegistrationRequest>
{
    public CreatePilotRegistrationValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(100);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20);

        RuleFor(x => x.DroneType)
            .NotEmpty();

        RuleFor(x => x.PilotLicenseNo)
            .NotEmpty();

        RuleFor(x => x.UsagePurpose)
            .NotEmpty();

        RuleFor(x => x.ExperienceYears)
            .GreaterThanOrEqualTo(0)
            .When(x => x.ExperienceYears.HasValue);

        RuleFor(x => x.DateOfBirth)
            .Must(BeAtLeast18YearsOld)
            .When(x => x.DateOfBirth.HasValue)
            .WithMessage("Pilot must be at least 18 years old.");

        RuleFor(x => x)
            .Must(HaveValidLicenseDates)
            .WithMessage(
                "License expiration date must be greater than issue date.");

        RuleFor(x => x.LicenseExpiredDate)
            .Must(NotBeExpired)
            .When(x => x.LicenseExpiredDate.HasValue)
            .WithMessage("Pilot license has expired.");
    }

    private static bool BeAtLeast18YearsOld(
        DateOnly? dateOfBirth)
    {
        if (!dateOfBirth.HasValue)
        {
            return true;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var age = today.Year - dateOfBirth.Value.Year;

        if (today < dateOfBirth.Value.AddYears(age))
        {
            age--;
        }

        return age >= 18;
    }

    private static bool HaveValidLicenseDates(
        CreatePilotRegistrationRequest request)
    {
        if (!request.LicenseIssuedDate.HasValue ||
            !request.LicenseExpiredDate.HasValue)
        {
            return true;
        }

        return request.LicenseExpiredDate >
               request.LicenseIssuedDate;
    }

    private static bool NotBeExpired(
        DateOnly? expiredDate)
    {
        if (!expiredDate.HasValue)
        {
            return true;
        }

        return expiredDate.Value >=
               DateOnly.FromDateTime(DateTime.UtcNow);
    }
}