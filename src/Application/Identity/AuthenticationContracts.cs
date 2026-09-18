using DocOps.Domain.Identity;
using FluentValidation;

namespace DocOps.Application.Identity;

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("O endereço de e-mail é obrigatório.")
            .MaximumLength(256)
            .EmailAddress().WithMessage("Indique um endereço de e-mail válido.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("A palavra-passe é obrigatória.")
            .MaximumLength(128);
    }
}

public sealed record DepartmentMembershipDto(Guid Id, string Name, string Code, bool IsPrimary);

public sealed record CurrentUserDto
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public required string Initials { get; init; }
    public string? JobTitle { get; init; }
    public required Guid TenantId { get; init; }
    public required string TenantName { get; init; }
    public required UserType UserType { get; init; }
    public required string PreferredCulture { get; init; }
    public bool IsPlatformAdministrator { get; init; }
    public DateTimeOffset? LastLoginAtUtc { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
    public required IReadOnlyList<DepartmentMembershipDto> Departments { get; init; }
}

public enum LoginFailureReason
{
    InvalidCredentials,
    LockedOut,
    AccountDisabled,
    TenantInactive
}

public sealed record LoginOutcome(bool Succeeded, CurrentUserDto? User, LoginFailureReason? FailureReason)
{
    public static LoginOutcome Success(CurrentUserDto user) => new(true, user, null);
    public static LoginOutcome Failure(LoginFailureReason reason) => new(false, null, reason);
}

public interface IUserSessionService
{
    Task<LoginOutcome> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-reads the caller's profile from the database. Returns null when the session is no longer valid.</summary>
    Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}
