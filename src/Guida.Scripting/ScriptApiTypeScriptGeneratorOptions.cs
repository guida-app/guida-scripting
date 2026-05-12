namespace Guida.Scripting;

/// <summary>
/// Options for generating TypeScript declarations from a <see cref="ScriptApiRegistry" />.
/// </summary>
public sealed record ScriptApiTypeScriptGeneratorOptions
{
    /// <summary>
    /// Name of the main interface that contains top-level functions and API groups.
    /// </summary>
    public string RootInterfaceName { get; init; } = "Guida";

    /// <summary>
    /// Name of the global API variable.
    /// </summary>
    public string GlobalVariableName { get; init; } = "g";

    /// <summary>
    /// Optional title emitted in the generated header.
    /// </summary>
    public string Title { get; init; } = "Guida Scripting API Type Definitions";

    /// <summary>
    /// Whether to include a short deterministic header.
    /// </summary>
    public bool IncludeHeader { get; init; } = true;

    /// <summary>
    /// Whether to include JSDoc comments from registry descriptions.
    /// </summary>
    public bool IncludeDescriptions { get; init; } = true;
}
