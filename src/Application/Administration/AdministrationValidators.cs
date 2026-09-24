using FluentValidation;

namespace DocOps.Application.Administration;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Indique o endereço de e-mail.")
            .EmailAddress().WithMessage("Endereço de e-mail inválido.")
            .MaximumLength(320);

        RuleFor(x => x.FirstName).NotEmpty().WithMessage("Indique o nome próprio.").MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Indique o apelido.").MaximumLength(100);
        RuleFor(x => x.JobTitle).MaximumLength(150);

        RuleFor(x => x.Roles)
            .Must(roles => roles.Count > 0)
            .WithMessage("Indique pelo menos um perfil: uma conta sem perfil entra e não faz nada.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("Indique o nome próprio.").MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Indique o apelido.").MaximumLength(100);
        RuleFor(x => x.JobTitle).MaximumLength(150);

        RuleFor(x => x.Roles)
            .Must(roles => roles.Count > 0)
            .WithMessage("Indique pelo menos um perfil.");
    }
}

public sealed class SaveDepartmentRequestValidator : AbstractValidator<SaveDepartmentRequest>
{
    public SaveDepartmentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Indique o nome do departamento.").MaximumLength(150);

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Indique o código.")
            .MaximumLength(20)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("O código só pode ter letras, números e hífenes.");

        RuleFor(x => x.Description).MaximumLength(500);
    }
}
