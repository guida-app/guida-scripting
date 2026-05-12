namespace Guida.Scripting;

/// <summary>
/// Describes an expected script search operation failure.
/// </summary>
public sealed record ScriptSearchError
{
    /// <summary>
    /// Creates a search error.
    /// </summary>
    public ScriptSearchError(
        ScriptSearchErrorCode code,
        string query,
        string? scope,
        string message)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Query = query;
        Scope = scope;
        Message = message;
    }

    /// <summary>
    /// Stable search error code.
    /// </summary>
    public ScriptSearchErrorCode Code { get; }

    /// <summary>
    /// Query related to the error.
    /// </summary>
    public string Query { get; }

    /// <summary>
    /// Optional search scope related to the error.
    /// </summary>
    public string? Scope { get; }

    /// <summary>
    /// Host-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Converts the search error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}
