using DocOps.Api.Infrastructure;
using DocOps.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocOps.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IUserSessionService sessionService) : ControllerBase
{
    /// <summary>Signs in with e-mail and password and issues the HTTP-only session cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        LoginOutcome outcome = await sessionService.LoginAsync(request, cancellationToken);

        if (outcome is { Succeeded: true, User: { } user })
        {
            return Ok(user);
        }

        (int status, string code, string detail) = outcome.FailureReason switch
        {
            LoginFailureReason.LockedOut => (
                StatusCodes.Status423Locked,
                "account_locked",
                "A conta está temporariamente bloqueada após várias tentativas falhadas. Tente novamente mais tarde."),

            LoginFailureReason.AccountDisabled => (
                StatusCodes.Status401Unauthorized,
                "account_disabled",
                "Esta conta está desativada. Contacte o administrador da sua organização."),

            LoginFailureReason.TenantInactive => (
                StatusCodes.Status401Unauthorized,
                "tenant_inactive",
                "A organização associada a esta conta está inativa."),

            // Wrong password and unknown e-mail deliberately return the same answer.
            _ => (
                StatusCodes.Status401Unauthorized,
                "invalid_credentials",
                "Credenciais inválidas.")
        };

        ProblemDetails problem = new()
        {
            Status = status,
            Title = "Autenticação falhou",
            Detail = detail,
            Type = $"https://docs.docops.local/errors/{code}"
        };

        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;

        return StatusCode(status, problem);
    }

    /// <summary>Ends the session and clears the cookie.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sessionService.LogoutAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Profile, roles, effective permissions and department memberships of the caller.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken cancellationToken)
    {
        CurrentUserDto? user = await sessionService.GetCurrentUserAsync(cancellationToken);

        if (user is null)
        {
            // The cookie is valid but the account no longer is (deactivated or deleted mid-session).
            await sessionService.LogoutAsync(cancellationToken);
            return Unauthorized();
        }

        return Ok(user);
    }
}
