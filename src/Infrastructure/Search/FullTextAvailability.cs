using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Search;

/// <summary>
/// Whether this server can answer a full-text query, asked once and remembered.
///
/// Full-text search is an optional SQL Server component. The container this project ships with has
/// it, but the one the integration tests start does not, and neither does every server a reviewer
/// might point the application at. Rather than assume, the search asks — and when the answer is no,
/// it matches by substring instead and says so in the response, so nobody mistakes a scan for a
/// ranked search.
/// </summary>
public sealed class FullTextAvailability(IServiceProvider services, ILogger<FullTextAvailability> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool? _enabled;

    /// <summary>
    /// True when the component is installed and the catalogue this application creates exists and is
    /// populated enough to query. Both halves matter: the component can be present while the
    /// migration that creates the indexes has not run, and querying then fails outright.
    /// </summary>
    public async ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (_enabled is { } known)
        {
            return known;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_enabled is { } raced)
            {
                return raced;
            }

            _enabled = await ProbeAsync(cancellationToken);
            logger.LogInformation(
                "Pesquisa de texto integral {State}.", _enabled.Value ? "disponível" : "indisponível");

            return _enabled.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the answer, so a server that gained the component is picked up on the next search.</summary>
    public void Forget() => _enabled = null;

    private async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        // A scope of its own: the probe outlives the request that first needed it, and the context
        // that request is using may already be disposed by the time a later search asks again.
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        DocOpsDbContext dbContext = scope.ServiceProvider.GetRequiredService<DocOpsDbContext>();

        try
        {
            int indexed = await dbContext.Database
                .SqlQuery<int>(
                    $"""
                     SELECT CASE
                         WHEN SERVERPROPERTY('IsFullTextInstalled') <> 1 THEN 0
                         WHEN NOT EXISTS (
                             SELECT 1 FROM sys.fulltext_indexes
                             WHERE object_id = OBJECT_ID('dbo.Documents')) THEN 0
                         ELSE 1
                     END AS Value
                     """)
                .SingleAsync(cancellationToken);

            return indexed == 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A server that cannot even answer the question cannot answer a full-text query either.
            logger.LogWarning(
                exception, "Não foi possível determinar se o texto integral está disponível; a assumir que não.");

            return false;
        }
    }
}
