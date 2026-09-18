using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DocOps.Api.Infrastructure;

/// <summary>
/// Validates every action argument that has a registered FluentValidation validator, before the
/// action runs. Validation therefore cannot be forgotten in a controller, and the failure shape is
/// identical across the whole API.
/// </summary>
public sealed class ValidationFilter(IServiceProvider serviceProvider) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (object? argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            Type validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (serviceProvider.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            FluentValidation.Results.ValidationResult result =
                await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
        }

        await next();
    }
}
