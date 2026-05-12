namespace Guida.Scripting;

/// <summary>
/// Options used when loading a script source document.
/// </summary>
public sealed record ScriptDocumentLoadOptions
{
    /// <summary>
    /// Optional language override for the loaded document.
    /// </summary>
    public ScriptLanguage? Language { get; init; }
}
