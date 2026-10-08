using DroneOps.Application.DTOs.Request.Missions;
using FluentValidation;

namespace DroneOps.Application.Validators.Missions;

public sealed class GenerateMissionSuggestionRequestValidator
    : AbstractValidator<GenerateMissionSuggestionRequest>
{
    public GenerateMissionSuggestionRequestValidator()
    {
        RuleFor(x => x.Prompt)
            .NotEmpty()
            .WithMessage("Prompt is required.")
            .MinimumLength(10)
            .WithMessage(
                "Prompt must contain at least 10 characters.")
            .MaximumLength(3000)
            .WithMessage(
                "Prompt must not exceed 3000 characters.");
    }
}