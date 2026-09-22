using FluentValidation;

namespace DocOps.Application.Search;

public sealed class NaturalLanguageSearchRequestValidator : AbstractValidator<NaturalLanguageSearchRequest>
{
    public NaturalLanguageSearchRequestValidator()
    {
        RuleFor(x => x.Question)
            .NotEmpty().WithMessage("Escreva a pergunta.")
            .MaximumLength(500).WithMessage("A pergunta não pode ter mais de 500 caracteres.");
    }
}
