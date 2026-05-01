namespace Guida.Scripting;

/// <summary>
/// Content read from a script store.
/// </summary>
public sealed record ScriptStoreValue
{
    /// <summary>
    /// Store key.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Stored content bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; init; }

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
