namespace Guida.Scripting;

/// <summary>
/// Describes an expected secret lookup failure.
/// </summary>
public sealed record ScriptSecretError
{
    /// <summary>
    /// Creates a secret lookup error.
    /// </summary>
    public ScriptSecretError(
        ScriptSecretErrorCode code,
        string name,
        string message)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Name = name;
        Message = message;
    }

    /// <summary>
    /// Stable secret error code.
    /// </summary>
    public ScriptSecretErrorCode Code { get; }

    /// <summary>
    /// Secret name related to the error.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Host-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Converts the secret error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}
