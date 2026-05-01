namespace Guida.Scripting;

/// <summary>
/// Request to search host-owned content.
/// </summary>
public sealed record ScriptSearchRequest
{
    /// <summary>
    /// Query text.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Optional host-defined search scope.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Optional maximum number of results to return.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Number of matching results to skip.
    /// </summary>
    public int Offset { get; init; }
}
