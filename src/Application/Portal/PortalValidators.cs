using FluentValidation;

namespace DocOps.Application.Portal;

public sealed class SubmitToPortalRequestValidator : AbstractValidator<SubmitToPortalRequest>
{
    public SubmitToPortalRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Indique o que está a submeter.")
            .MaximumLength(300);

        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class ProvideInformationRequestValidator : AbstractValidator<ProvideInformationRequest>
{
    public ProvideInformationRequestValidator()
    {
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("Escreva a sua resposta.")
            .MaximumLength(2000);
    }
}
