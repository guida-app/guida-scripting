namespace Guida.Scripting;

/// <summary>
/// Result of a script store operation that returns a value.
/// </summary>
public sealed record ScriptStoreResult<T>
{
    private ScriptStoreResult(bool success, T? value, ScriptStoreError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Whether the store operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Returned value when the operation succeeded.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptStoreError? Error { get; }

    /// <summary>
    /// Creates a successful store result.
    /// </summary>
    public static ScriptStoreResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed store result.
    /// </summary>
    public static ScriptStoreResult<T> Failed(ScriptStoreError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptStoreResult<T>(false, default, error);
    }
}
