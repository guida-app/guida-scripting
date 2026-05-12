namespace Guida.Scripting;

/// <summary>
/// Result of a script search operation that returns a value.
/// </summary>
public sealed record ScriptSearchResult<T>
{
    private ScriptSearchResult(bool success, T? value, ScriptSearchError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Whether the search operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Returned value when the operation succeeded.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Error information when the operation failed.
    /// </summary>
    public ScriptSearchError? Error { get; }

    /// <summary>
    /// Creates a successful search result.
    /// </summary>
    public static ScriptSearchResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed search result.
    /// </summary>
    public static ScriptSearchResult<T> Failed(ScriptSearchError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ScriptSearchResult<T>(false, default, error);
    }
}
