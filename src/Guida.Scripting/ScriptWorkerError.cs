namespace Guida.Scripting;

/// <summary>
/// Describes an expected script worker operation failure.
/// </summary>
public sealed record ScriptWorkerError
{
    /// <summary>
    /// Creates a worker error.
    /// </summary>
    public ScriptWorkerError(
        ScriptWorkerErrorCode code,
        string workerName,
        string jobId,
        string message)
    {
        ArgumentNullException.ThrowIfNull(workerName);
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        WorkerName = workerName;
        JobId = jobId;
        Message = message;
    }

    /// <summary>
    /// Stable worker error code.
    /// </summary>
    public ScriptWorkerErrorCode Code { get; }

    /// <summary>
    /// Worker name related to the error.
    /// </summary>
    public string WorkerName { get; }

    /// <summary>
    /// Worker job id related to the error.
    /// </summary>
    public string JobId { get; }

    /// <summary>
    /// Host-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Converts the worker error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}
