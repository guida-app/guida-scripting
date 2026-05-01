namespace Guida.Scripting;

/// <summary>
/// Result of a workspace operation that returns a value.
/// </summary>
public sealed record ScriptWorkspaceResult<T>
{
    private ScriptWorkspaceResult(bool success, T? value, ScriptWorkspaceError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Whether the workspace operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Returned value when the operation succeeded.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptWorkspaceError? Error { get; }

    /// <summary>
    /// Creates a successful workspace result.
    /// </summary>
    public static ScriptWorkspaceResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed workspace result.
    /// </summary>
    public static ScriptWorkspaceResult<T> Failed(ScriptWorkspaceError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptWorkspaceResult<T>(false, default, error);
    }
}
