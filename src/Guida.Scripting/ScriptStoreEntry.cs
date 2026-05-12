namespace Guida.Scripting;

/// <summary>
/// Metadata for one script store entry.
/// </summary>
public sealed record ScriptStoreEntry
{
    /// <summary>
    /// Store key.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Stored content length in bytes.
    /// </summary>
    public long Length { get; init; }

    /// <summary>
    /// Optional stored content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Optional creation timestamp.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Optional last updated timestamp.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
