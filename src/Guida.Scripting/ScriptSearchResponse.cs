namespace Guida.Scripting;

/// <summary>
/// Response returned from a search operation.
/// </summary>
public sealed record ScriptSearchResponse
{
    /// <summary>
    /// Returned result items.
    /// </summary>
    public IReadOnlyList<ScriptSearchItem> Items { get; init; } = Array.Empty<ScriptSearchItem>();

    /// <summary>
    /// Total matching item count when known.
    /// </summary>
    public int? TotalCount { get; init; }

    /// <summary>
    /// Search elapsed time.
    /// </summary>
    public TimeSpan Elapsed { get; init; }
}
