namespace Guida.Scripting;

/// <summary>
/// Metadata for a logical workspace entry.
/// </summary>
public sealed record ScriptWorkspaceEntry
{
    /// <summary>
    /// Normalized logical workspace path.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Display name of the entry.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Kind of workspace entry.
    /// </summary>
    public ScriptWorkspaceEntryKind Kind { get; init; }

    /// <summary>
    /// Optional byte length for file entries.
    /// </summary>
    public long? Length { get; init; }

    /// <summary>
    /// Optional last modified timestamp.
    /// </summary>
    public DateTimeOffset? LastModifiedAt { get; init; }
}
