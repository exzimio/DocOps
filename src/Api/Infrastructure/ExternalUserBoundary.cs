using DocOps.Application.Abstractions;
using DocOps.Domain.Common;
using DocOps.Domain.Identity;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DocOps.Api.Infrastructure;

/// <summary>Marks a controller that an external account may call: the portal and its own session.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowExternalUsersAttribute : Attribute;

/// <summary>
/// External accounts reach only the controllers marked with <see cref="AllowExternalUsersAttribute"/>,
/// and every other endpoint refuses them, including the ones added later. Their role keeps
/// <c>documents.view</c>, <c>create</c> and <c>download</c>, because the portal service uses them on
/// the server, so permissions alone would have opened the internal document, tenant and notification
/// endpoints to a supplier.
/// </summary>
public sealed class ExternalUserBoundaryFilter(ICurrentUser currentUser) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true || currentUser.UserType != UserType.External)
        {
            return;
        }

        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowExternalUsersAttribute>().Any())
        {
            return;
        }

        throw new ForbiddenOperationException("Esta conta só tem acesso ao portal.");
    }
}
