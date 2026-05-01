namespace Guida.Scripting;

/// <summary>
/// Result of a script worker operation that does not return a value.
/// </summary>
public sealed record ScriptWorkerResult
{
    private ScriptWorkerResult(bool success, ScriptWorkerError? error)
    {
        Success = success;
        Error = error;
    }

    /// <summary>
    /// Whether the worker operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptWorkerError? Error { get; }

    /// <summary>
    /// Creates a successful worker result.
    /// </summary>
    public static ScriptWorkerResult Succeeded() => new(true, null);

    /// <summary>
    /// Creates a failed worker result.
    /// </summary>
    public static ScriptWorkerResult Failed(ScriptWorkerError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptWorkerResult(false, error);
    }
}
