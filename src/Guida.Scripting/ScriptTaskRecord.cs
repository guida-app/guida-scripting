namespace Guida.Scripting;

/// <summary>
/// Public snapshot of a script task lifecycle.
/// </summary>
public sealed record ScriptTaskRecord
{
    /// <summary>
    /// Unique task identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name for the task.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Where the task came from.
    /// </summary>
    public ScriptTaskOrigin Origin { get; init; } = ScriptTaskOrigin.User;

    /// <summary>
    /// The script language used by the task.
    /// </summary>
    public ScriptLanguage Language { get; init; } = ScriptLanguage.Unknown;

    /// <summary>
    /// Current task status.
    /// </summary>
    public ScriptTaskStatus Status { get; init; } = ScriptTaskStatus.Running;

    /// <summary>
    /// When the task started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When the task ended, when it has reached a terminal status.
    /// </summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>
    /// How long the task ran, when it has reached a terminal status.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Error text for failed, canceled, or timed-out tasks.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Values returned by the script runtime.
    /// </summary>
    public IReadOnlyList<object?> ReturnValues { get; init; } = Array.Empty<object?>();

    /// <summary>
    /// Timeout requested for this task.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Optional script display name, file name, or logical module path.
    /// </summary>
    public string? ScriptName { get; init; }
}
