namespace Guida.Scripting;

/// <summary>
/// Describes an expected script store operation failure.
/// </summary>
public sealed record ScriptStoreError
{
    /// <summary>
    /// Creates a store error.
    /// </summary>
    public ScriptStoreError(
        ScriptStoreErrorCode code,
        string key,
        string message)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Key = key;
        Message = message;
    }

    /// <summary>
    /// Stable store error code.
    /// </summary>
    public ScriptStoreErrorCode Code { get; }

    /// <summary>
    /// Store key related to the error.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Host-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Converts the store error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}
