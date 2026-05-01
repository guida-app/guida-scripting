namespace Guida.Scripting;

/// <summary>
/// Host-mediated worker capability for background work owned by the host.
/// </summary>
public interface IScriptWorker : IScriptHostCapability
{
    /// <summary>
    /// Starts host-managed worker work.
    /// </summary>
    Task<ScriptWorkerResult<ScriptWorkerJob>> StartAsync(
        ScriptWorkerRequest request,
        ScriptWorkerStartOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one worker job by id.
    /// </summary>
    Task<ScriptWorkerResult<ScriptWorkerJob>> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation of one worker job.
    /// </summary>
    Task<ScriptWorkerResult<ScriptWorkerJob>> CancelAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists current or recent worker jobs.
    /// </summary>
    Task<ScriptWorkerResult<IReadOnlyList<ScriptWorkerJob>>> ListAsync(
        CancellationToken cancellationToken = default);
}
