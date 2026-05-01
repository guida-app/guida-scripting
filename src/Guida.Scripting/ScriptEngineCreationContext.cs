namespace Guida.Scripting;

/// <summary>
/// Provides context when creating a script engine.
/// </summary>
public sealed record ScriptEngineCreationContext
{
    /// <summary>
    /// The language the engine must execute.
    /// </summary>
    public ScriptLanguage Language { get; init; } = ScriptLanguage.Unknown;

    /// <summary>
    /// Optional display name for diagnostics.
    /// </summary>
    public string? Name { get; init; }
}
