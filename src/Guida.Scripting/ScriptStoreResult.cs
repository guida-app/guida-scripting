namespace Guida.Scripting;

/// <summary>
/// Result of a script store operation that does not return a value.
/// </summary>
public sealed record ScriptStoreResult
{
    private ScriptStoreResult(bool success, ScriptStoreError? error)
    {
        Success = success;
        Error = error;
    }

    /// <summary>
    /// Whether the store operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptStoreError? Error { get; }

    /// <summary>
    /// Creates a successful store result.
    /// </summary>
    public static ScriptStoreResult Succeeded() => new(true, null);

    /// <summary>
    /// Creates a failed store result.
    /// </summary>
    public static ScriptStoreResult Failed(ScriptStoreError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptStoreResult(false, error);
    }
}
