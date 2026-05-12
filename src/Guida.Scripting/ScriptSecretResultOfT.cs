namespace Guida.Scripting;

/// <summary>
/// Result of a secret operation that returns a value.
/// </summary>
public sealed record ScriptSecretResult<T>
{
    private ScriptSecretResult(bool success, T? value, ScriptSecretError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Whether the secret operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Returned value when the operation succeeded.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptSecretError? Error { get; }

    /// <summary>
    /// Creates a successful secret result.
    /// </summary>
    public static ScriptSecretResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed secret result.
    /// </summary>
    public static ScriptSecretResult<T> Failed(ScriptSecretError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptSecretResult<T>(false, default, error);
    }
}
