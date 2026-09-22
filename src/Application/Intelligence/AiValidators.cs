using DocOps.Domain.Intelligence;
using FluentValidation;

namespace DocOps.Application.Intelligence;

public sealed class DecideFieldRequestValidator : AbstractValidator<DecideFieldRequest>
{
    public DecideFieldRequestValidator()
    {
        RuleFor(x => x.Decision)
            .IsInEnum()
            .NotEqual(AiSuggestionDecision.Pending).WithMessage("Indique uma decisão.");

        RuleFor(x => x.Value)
            .NotEmpty().When(x => x.Decision == AiSuggestionDecision.Edited)
            .WithMessage("Indique o valor corrigido.")
            .MaximumLength(1000);
    }
}

public sealed class AskDocumentRequestValidator : AbstractValidator<AskDocumentRequest>
{
    public AskDocumentRequestValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();

        RuleFor(x => x.Question)
            .NotEmpty().WithMessage("Escreva uma pergunta.")
            .MinimumLength(3).WithMessage("A pergunta é demasiado curta.")
            .MaximumLength(500);
    }
}
