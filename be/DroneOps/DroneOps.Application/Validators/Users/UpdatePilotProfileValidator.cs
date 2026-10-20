using DroneOps.Application.DTOs.Request.Users;
using FluentValidation;

namespace DroneOps.Application.Validators.Users;

public sealed class UpdatePilotProfileValidator
    : AbstractValidator<UpdatePilotProfileRequest>
{
    public UpdatePilotProfileValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20);

        RuleFor(x => x.ExperienceYears)
            .GreaterThanOrEqualTo(0)
            .WithMessage(
                "Experience years cannot be negative.");

        RuleFor(x => x.DateOfBirth)
            .Must(BeAtLeast18YearsOld)
            .When(x => x.DateOfBirth.HasValue)
            .WithMessage(
                "Pilot must be at least 18 years old.");

        When(HasLicenseUpdate, () =>
        {
            RuleFor(x => x.PilotLicenseNo)
                .NotEmpty()
                .WithMessage(
                    "Pilot license number is required.")
                .MaximumLength(255);

            RuleFor(x => x.LicenseIssuedDate)
                .NotNull()
                .WithMessage(
                    "License issued date is required.");

            RuleFor(x => x.LicenseExpiredDate)
                .NotNull()
                .WithMessage(
                    "License expiration date is required.");

            RuleFor(x => x)
                .Must(HaveValidLicenseDates)
                .WithMessage(
                    "License expiration date must be greater than issue date.");

            RuleFor(x => x.LicenseExpiredDate)
                .Must(NotBeExpired)
                .When(x => x.LicenseExpiredDate.HasValue)
                .WithMessage(
                    "Pilot license has expired.");

            RuleFor(x => x.LicenseImageUrls)
                .NotNull()
                .Must(x => x is { Count: > 0 })
                .WithMessage(
                    "At least one license image is required.");

            RuleForEach(x => x.LicenseImageUrls!)
                .NotEmpty()
                .WithMessage(
                    "License image URL cannot be empty.")
                .MaximumLength(2000);
        });
    }

    private static bool HasLicenseUpdate(
        UpdatePilotProfileRequest request)
    {
        return
            !string.IsNullOrWhiteSpace(
                request.PilotLicenseNo) ||
            request.LicenseIssuedDate.HasValue ||
            request.LicenseExpiredDate.HasValue ||
            request.LicenseImageUrls is { Count: > 0 };
    }

    private static bool BeAtLeast18YearsOld(
        DateOnly? dateOfBirth)
    {
        if (!dateOfBirth.HasValue)
        {
            return true;
        }

        DateOnly today =
            DateOnly.FromDateTime(DateTime.UtcNow);

        int age =
            today.Year - dateOfBirth.Value.Year;

        if (today < dateOfBirth.Value.AddYears(age))
        {
            age--;
        }

        return age >= 18;
    }

    private static bool HaveValidLicenseDates(
        UpdatePilotProfileRequest request)
    {
        if (!request.LicenseIssuedDate.HasValue ||
            !request.LicenseExpiredDate.HasValue)
        {
            return true;
        }

        return request.LicenseExpiredDate.Value >
               request.LicenseIssuedDate.Value;
    }

    private static bool NotBeExpired(
        DateOnly? expiredDate)
    {
        if (!expiredDate.HasValue)
        {
            return true;
        }

        DateOnly today =
            DateOnly.FromDateTime(DateTime.UtcNow);

        return expiredDate.Value >= today;
    }
}