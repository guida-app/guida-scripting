namespace Guida.Scripting;

/// <summary>
/// Result of a workspace operation that does not return a value.
/// </summary>
public sealed record ScriptWorkspaceResult
{
    private ScriptWorkspaceResult(bool success, ScriptWorkspaceError? error)
    {
        Success = success;
        Error = error;
    }

    /// <summary>
    /// Whether the workspace operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptWorkspaceError? Error { get; }

    /// <summary>
    /// Creates a successful workspace result.
    /// </summary>
    public static ScriptWorkspaceResult Succeeded() => new(true, null);

    /// <summary>
    /// Creates a failed workspace result.
    /// </summary>
    public static ScriptWorkspaceResult Failed(ScriptWorkspaceError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptWorkspaceResult(false, error);
    }
}
