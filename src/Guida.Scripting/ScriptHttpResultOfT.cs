namespace Guida.Scripting;

/// <summary>
/// Result of an HTTP capability operation that returns a value.
/// </summary>
public sealed record ScriptHttpResult<T>
{
    private ScriptHttpResult(bool success, T? value, ScriptHttpError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Whether the HTTP operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Returned value when the operation succeeded.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptHttpError? Error { get; }

    /// <summary>
    /// Creates a successful HTTP result.
    /// </summary>
    public static ScriptHttpResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed HTTP result.
    /// </summary>
    public static ScriptHttpResult<T> Failed(ScriptHttpError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptHttpResult<T>(false, default, error);
    }
}
