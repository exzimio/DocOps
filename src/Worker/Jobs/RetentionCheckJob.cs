using DocOps.Infrastructure.Compliance;

namespace DocOps.Worker.Jobs;

/// <summary>
/// Agenda a verificação de retenção. O trabalho verdadeiro está em <see cref="RetentionChecker"/>,
/// como no caso da fila de IA: o que corre periodicamente é fino de propósito, para a regra poder
/// ser exercitada por um teste sem levantar um worker atrás.
/// </summary>
public sealed class RetentionCheckJob(RetentionChecker checker, ILogger<RetentionCheckJob> logger)
    : IBackgroundJob
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        int announced = await checker.AnnounceExpiredAsync(cancellationToken);

        if (announced > 0)
        {
            logger.LogInformation("Retention: {Count} document(s) announced", announced);
        }

        return announced;
    }
}
