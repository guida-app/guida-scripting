namespace Guida.Scripting;

/// <summary>
/// Identifies the script task currently associated with a host context.
/// </summary>
public sealed record ScriptExecutionInfo
{
    /// <summary>
    /// Empty execution metadata used when no task is associated with the host context.
    /// </summary>
    public static ScriptExecutionInfo Empty { get; } = new();

    /// <summary>
    /// Identifier of the task executing through this host context.
    /// </summary>
    public string? TaskId { get; init; }

    /// <summary>
    /// Origin of the task executing through this host context.
    /// </summary>
    public ScriptTaskOrigin Origin { get; init; } = ScriptTaskOrigin.User;

    /// <summary>
    /// Host policy metadata for this execution.
    /// </summary>
    public ScriptExecutionPolicy Policy { get; init; } = ScriptExecutionPolicy.Trusted;
}
