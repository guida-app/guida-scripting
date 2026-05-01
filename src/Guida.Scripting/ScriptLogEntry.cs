namespace Guida.Scripting;

/// <summary>
/// One log entry emitted during script execution.
/// </summary>
public sealed record ScriptLogEntry
{
    /// <summary>
    /// When the entry was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Entry severity.
    /// </summary>
    public ScriptLogLevel Level { get; init; } = ScriptLogLevel.Information;

    /// <summary>
    /// Log message text.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Optional category or subsystem name.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional script task identifier associated with this entry.
    /// </summary>
    public string? TaskId { get; init; }
}
