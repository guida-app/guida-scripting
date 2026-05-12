namespace Guida.Scripting;

/// <summary>
/// Document registered with the in-memory search capability.
/// </summary>
public sealed record ScriptInMemorySearchDocument
{
    /// <summary>
    /// Search result item returned when the document matches.
    /// </summary>
    public ScriptSearchItem Item { get; init; } = new();

    /// <summary>
    /// Additional text used only for matching.
    /// </summary>
    public string? SearchText { get; init; }
}
