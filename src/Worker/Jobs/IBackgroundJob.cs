namespace DocOps.Worker.Jobs;

/// <summary>
/// One unit of recurring background work.
///
/// The contract exists so the runner can treat the four jobs alike — run each in its own scope, time
/// it, count its outcome — instead of naming each one separately and forgetting the next one.
/// </summary>
public interface IBackgroundJob
{
    /// <summary>Runs one pass and returns how many items it handled.</summary>
    Task<int> RunAsync(CancellationToken cancellationToken);
}
