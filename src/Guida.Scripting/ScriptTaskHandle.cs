namespace Guida.Scripting;

/// <summary>
/// Handle returned when a script task is started without waiting for completion.
/// </summary>
public sealed record ScriptTaskHandle
{
    /// <summary>
    /// Unique task identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Initial running task snapshot.
    /// </summary>
    public ScriptTaskRecord InitialRecord { get; init; } = new();

    /// <summary>
    /// Final task record once execution reaches a terminal status.
    /// </summary>
    public Task<ScriptTaskRecord> Completion { get; init; } =
        Task.FromResult(new ScriptTaskRecord());
}
