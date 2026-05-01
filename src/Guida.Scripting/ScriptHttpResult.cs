namespace Guida.Scripting;

/// <summary>
/// Result of an HTTP capability operation that does not return a value.
/// </summary>
public sealed record ScriptHttpResult
{
    private ScriptHttpResult(bool success, ScriptHttpError? error)
    {
        Success = success;
        Error = error;
    }

    /// <summary>
    /// Whether the HTTP operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptHttpError? Error { get; }

    /// <summary>
    /// Creates a successful HTTP result.
    /// </summary>
    public static ScriptHttpResult Succeeded() => new(true, null);

    /// <summary>
    /// Creates a failed HTTP result.
    /// </summary>
    public static ScriptHttpResult Failed(ScriptHttpError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptHttpResult(false, error);
    }
}
