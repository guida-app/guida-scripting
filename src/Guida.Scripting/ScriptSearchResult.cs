namespace Guida.Scripting;

/// <summary>
/// Result of a script search operation that does not return a value.
/// </summary>
public sealed record ScriptSearchResult
{
    private ScriptSearchResult(bool success, ScriptSearchError? error)
    {
        Success = success;
        Error = error;
    }

    /// <summary>
    /// Whether the search operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptSearchError? Error { get; }

    /// <summary>
    /// Creates a successful search result.
    /// </summary>
    public static ScriptSearchResult Succeeded() => new(true, null);

    /// <summary>
    /// Creates a failed search result.
    /// </summary>
    public static ScriptSearchResult Failed(ScriptSearchError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptSearchResult(false, error);
    }
}
