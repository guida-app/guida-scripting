namespace Guida.Scripting;

/// <summary>
/// Describes an expected script document loading failure.
/// </summary>
public sealed record ScriptDocumentError
{
    /// <summary>
    /// Creates a document loading error.
    /// </summary>
    public ScriptDocumentError(
        ScriptDocumentErrorCode code,
        string documentId,
        string message)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        DocumentId = documentId;
        Message = message;
    }

    /// <summary>
    /// Stable document error code.
    /// </summary>
    public ScriptDocumentErrorCode Code { get; }

    /// <summary>
    /// Document identifier related to the error.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    /// Host-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Converts the document error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}
