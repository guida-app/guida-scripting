namespace Guida.Scripting;

/// <summary>
/// Result of a script queue operation that does not return a value.
/// </summary>
public sealed record ScriptQueueResult
{
    private ScriptQueueResult(bool success, ScriptQueueError? error)
    {
        Success = success;
        Error = error;
    }

    /// <summary>
    /// Whether the queue operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptQueueError? Error { get; }

    /// <summary>
    /// Creates a successful queue result.
    /// </summary>
    public static ScriptQueueResult Succeeded() => new(true, null);

    /// <summary>
    /// Creates a failed queue result.
    /// </summary>
    public static ScriptQueueResult Failed(ScriptQueueError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptQueueResult(false, error);
    }
}
