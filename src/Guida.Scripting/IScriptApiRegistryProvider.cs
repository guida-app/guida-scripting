namespace Guida.Scripting;

/// <summary>
/// Provides script-facing API metadata for editor tooling, generated definitions, and host adapters.
/// </summary>
public interface IScriptApiRegistryProvider
{
    /// <summary>
    /// Gets the registry exposed by this provider.
    /// </summary>
    ScriptApiRegistry GetRegistry();
}
