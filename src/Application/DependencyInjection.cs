using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DocOps.Application;

public static class DependencyInjection
{
    /// <summary>Registers application-layer services. The application layer owns no infrastructure concerns.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly(), includeInternalTypes: true);
        return services;
    }
}
