namespace Guida.Scripting;

/// <summary>
/// Options used when setting a script store value.
/// </summary>
public sealed record ScriptStoreSetOptions
{
    /// <summary>
    /// Whether an existing store entry may be overwritten.
    /// </summary>
    public bool Overwrite { get; init; } = true;

    /// <summary>
    /// Optional stored content type.
    /// </summary>
    public string? ContentType { get; init; }
}
