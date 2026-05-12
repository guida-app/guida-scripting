namespace Guida.Scripting;

/// <summary>
/// Describes an expected workspace operation failure.
/// </summary>
public sealed record ScriptWorkspaceError
{
    /// <summary>
    /// Creates a workspace error.
    /// </summary>
    public ScriptWorkspaceError(
        ScriptWorkspaceErrorCode code,
        string path,
        string message)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Path = path;
        Message = message;
    }

    /// <summary>
    /// Stable workspace error code.
    /// </summary>
    public ScriptWorkspaceErrorCode Code { get; }

    /// <summary>
    /// Logical workspace path related to the error.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Host-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Converts the workspace error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}
