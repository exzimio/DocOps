using DocOps.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Api.Infrastructure;

/// <summary>
/// Turns every unhandled exception into a RFC 9457 ProblemDetails response. Internal failures never
/// leak a stack trace or an SQL message to the client; the correlation id is the bridge to the logs.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Nginx's 499 "client closed request"; not part of the framework's status code constants.</summary>
    private const int ClientClosedRequest = 499;


    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int status, string title, string detail, string code) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning("Request failed on {Method} {Path}: {Message}", httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }

        httpContext.Response.StatusCode = status;

        ProblemDetails problemDetails = new()
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://docs.docops.local/errors/{code}",
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        problemDetails.Extensions["code"] = code;
        problemDetails.Extensions["correlationId"] = httpContext.TraceIdentifier;

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static (int Status, string Title, string Detail, string Code) Map(Exception exception) => exception switch
    {
        ValidationException => (
            StatusCodes.Status400BadRequest,
            "Pedido inválido",
            "Um ou mais campos não passaram a validação.",
            "validation_failed"),

        EntityNotFoundException notFound => (
            StatusCodes.Status404NotFound,
            "Não encontrado",
            notFound.Message,
            notFound.Code),

        ForbiddenOperationException forbidden => (
            StatusCodes.Status403Forbidden,
            "Acesso negado",
            forbidden.Message,
            forbidden.Code),

        DomainException domain => (
            StatusCodes.Status409Conflict,
            "Operação não permitida",
            domain.Message,
            domain.Code),

        DbUpdateConcurrencyException => (
            StatusCodes.Status409Conflict,
            "Conflito de concorrência",
            "O registo foi alterado por outro utilizador. Recarregue e tente novamente.",
            "concurrency_conflict"),

        OperationCanceledException => (
            ClientClosedRequest,
            "Pedido cancelado",
            "O pedido foi cancelado pelo cliente.",
            "request_cancelled"),

        _ => (
            StatusCodes.Status500InternalServerError,
            "Erro interno",
            "Ocorreu um erro inesperado. Consulte o identificador de correlação para diagnóstico.",
            "internal_error")
    };
}
