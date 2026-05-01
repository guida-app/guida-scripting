namespace Guida.Scripting;

/// <summary>
/// Host-provided context available to script engines.
/// </summary>
public sealed record ScriptHostContext
{
    /// <summary>
    /// Empty host context used when a host does not provide runtime services.
    /// </summary>
    public static ScriptHostContext Empty { get; } = new();

    /// <summary>
    /// Optional logger engines can use for script-visible or host-visible messages.
    /// </summary>
    public IScriptLogger? Logger { get; init; }
}
