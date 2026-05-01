namespace Guida.Scripting;

/// <summary>
/// Options used when starting a script task.
/// </summary>
public sealed record ScriptTaskStartOptions
{
    /// <summary>
    /// Optional display name for the task.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Where the task came from.
    /// </summary>
    public ScriptTaskOrigin Origin { get; init; } = ScriptTaskOrigin.User;

    /// <summary>
    /// Optional timeout override for this task.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Optional language override for this task.
    /// </summary>
    public ScriptLanguage? Language { get; init; }
}
