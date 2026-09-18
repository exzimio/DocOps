using FluentValidation;

namespace DocOps.Application.Documents;

public sealed class CreateDocumentRequestValidator : AbstractValidator<CreateDocumentRequest>
{
    public CreateDocumentRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("O título é obrigatório.")
            .MaximumLength(300);

        RuleFor(x => x.DocumentTypeId)
            .NotEmpty().WithMessage("Selecione o tipo de documento.");

        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.BusinessEntity).MaximumLength(200);

        RuleFor(x => x.Tags)
            .Must(tags => tags.Count <= 20).WithMessage("Um documento não pode ter mais de 20 etiquetas.");

        RuleForEach(x => x.Tags)
            .NotEmpty().WithMessage("Uma etiqueta não pode ser vazia.")
            .MaximumLength(60);
    }
}

public sealed class UpdateDocumentRequestValidator : AbstractValidator<UpdateDocumentRequest>
{
    public UpdateDocumentRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("O título é obrigatório.")
            .MaximumLength(300);

        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.BusinessEntity).MaximumLength(200);

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("Token de concorrência em falta. Recarregue o documento.");

        RuleFor(x => x.Tags)
            .Must(tags => tags.Count <= 20).WithMessage("Um documento não pode ter mais de 20 etiquetas.");

        RuleForEach(x => x.Tags).NotEmpty().MaximumLength(60);
    }
}

public sealed class ChangeDocumentStatusRequestValidator : AbstractValidator<ChangeDocumentStatusRequest>
{
    public ChangeDocumentStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum().WithMessage("Estado inválido.");
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}

public sealed class GrantDocumentPermissionRequestValidator : AbstractValidator<GrantDocumentPermissionRequest>
{
    public GrantDocumentPermissionRequestValidator()
    {
        RuleFor(x => x.SubjectType).IsInEnum();

        RuleFor(x => x.AccessLevels)
            .NotEmpty().WithMessage("Indique pelo menos um nível de acesso.");

        RuleForEach(x => x.AccessLevels)
            .Must(AccessLevelNames.IsKnown)
            .WithMessage("Nível de acesso desconhecido.");

        RuleFor(x => x.SubjectId)
            .NotNull().WithMessage("Selecione o utilizador ou departamento.")
            .When(x => x.SubjectType is Domain.Documents.PermissionSubjectType.User
                or Domain.Documents.PermissionSubjectType.Department);

        RuleFor(x => x.RoleName)
            .NotEmpty().WithMessage("Selecione o perfil.")
            .MaximumLength(100)
            .When(x => x.SubjectType is Domain.Documents.PermissionSubjectType.Role);

        RuleFor(x => x.ExpiresAtUtc)
            .Must(expiry => expiry is null || expiry > DateTimeOffset.UtcNow)
            .WithMessage("A data de expiração tem de ser futura.");
    }
}

public sealed class DocumentSearchRequestValidator : AbstractValidator<DocumentSearchRequest>
{
    public DocumentSearchRequestValidator()
    {
        RuleFor(x => x.Query).MaximumLength(200);

        RuleFor(x => x)
            .Must(request => request.CreatedFromUtc is null
                || request.CreatedToUtc is null
                || request.CreatedFromUtc <= request.CreatedToUtc)
            .WithMessage("A data inicial não pode ser posterior à data final.")
            .WithName(nameof(DocumentSearchRequest.CreatedFromUtc));
    }
}
