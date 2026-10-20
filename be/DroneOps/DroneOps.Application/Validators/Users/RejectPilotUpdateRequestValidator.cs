using DroneOps.Application.DTOs.Request.Users;
using FluentValidation;

namespace DroneOps.Application.Validators.Users;

public sealed class RejectPilotUpdateRequestValidator
    : AbstractValidator<RejectPilotUpdateRequest>
{
    public RejectPilotUpdateRequestValidator()
    {
        RuleFor(x => x.RejectReason)
            .NotEmpty()
            .WithMessage(
                "Reject reason is required.")
            .MaximumLength(1000);
    }
}