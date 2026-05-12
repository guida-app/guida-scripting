namespace Guida.Scripting;

/// <summary>
/// One search result item.
/// </summary>
public sealed record ScriptSearchItem
{
    /// <summary>
    /// Host-owned result id.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Result title.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Optional result summary.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Optional result URI.
    /// </summary>
    public Uri? Uri { get; init; }

    /// <summary>
    /// Optional provider-specific score.
    /// </summary>
    public double? Score { get; init; }

    /// <summary>
    /// Optional result content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Optional source or scope name.
    /// </summary>
    public string? SourceName { get; init; }
}
