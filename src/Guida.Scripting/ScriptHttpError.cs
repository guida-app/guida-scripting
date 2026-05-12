namespace Guida.Scripting;

/// <summary>
/// Describes an expected HTTP capability failure.
/// </summary>
public sealed record ScriptHttpError
{
    /// <summary>
    /// Creates an HTTP capability error.
    /// </summary>
    public ScriptHttpError(
        ScriptHttpErrorCode code,
        Uri? uri,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Uri = uri;
        Message = message;
    }

    /// <summary>
    /// Stable HTTP error code.
    /// </summary>
    public ScriptHttpErrorCode Code { get; }

    /// <summary>
    /// Request URI related to the error, when available.
    /// </summary>
    public Uri? Uri { get; }

    /// <summary>
    /// Host-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Converts the HTTP error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}
