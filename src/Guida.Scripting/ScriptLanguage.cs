namespace Guida.Scripting;

/// <summary>
/// Identifies a scripting language understood by a host.
/// </summary>
public enum ScriptLanguage
{
    /// <summary>
    /// The language could not be determined.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// JavaScript source.
    /// </summary>
    JavaScript,

    /// <summary>
    /// TypeScript source.
    /// </summary>
    TypeScript,

    /// <summary>
    /// Lua source.
    /// </summary>
    Lua,

    /// <summary>
    /// Janet source.
    /// </summary>
    Janet
}
