namespace Guida.Scripting;

/// <summary>
/// Result of a script document operation that returns a value.
/// </summary>
public sealed record ScriptDocumentResult<T>
{
    private ScriptDocumentResult(bool success, T? value, ScriptDocumentError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Whether the document operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Returned value when the operation succeeded.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptDocumentError? Error { get; }

    /// <summary>
    /// Creates a successful document result.
    /// </summary>
    public static ScriptDocumentResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed document result.
    /// </summary>
    public static ScriptDocumentResult<T> Failed(ScriptDocumentError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptDocumentResult<T>(false, default, error);
    }
}
