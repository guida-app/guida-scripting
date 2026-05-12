namespace Guida.Scripting;

/// <summary>
/// Result of a script worker operation that returns a value.
/// </summary>
public sealed record ScriptWorkerResult<T>
{
    private ScriptWorkerResult(bool success, T? value, ScriptWorkerError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Whether the worker operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Returned value when the operation succeeded.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptWorkerError? Error { get; }

    /// <summary>
    /// Creates a successful worker result.
    /// </summary>
    public static ScriptWorkerResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed worker result.
    /// </summary>
    public static ScriptWorkerResult<T> Failed(ScriptWorkerError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptWorkerResult<T>(false, default, error);
    }
}
