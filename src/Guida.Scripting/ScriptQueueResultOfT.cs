namespace Guida.Scripting;

/// <summary>
/// Result of a script queue operation that returns a value.
/// </summary>
public sealed record ScriptQueueResult<T>
{
    private ScriptQueueResult(bool success, T? value, ScriptQueueError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Whether the queue operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Returned value when the operation succeeded.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptQueueError? Error { get; }

    /// <summary>
    /// Creates a successful queue result.
    /// </summary>
    public static ScriptQueueResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed queue result.
    /// </summary>
    public static ScriptQueueResult<T> Failed(ScriptQueueError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptQueueResult<T>(false, default, error);
    }
}
