namespace Guida.Scripting;

/// <summary>
/// Content read from a logical workspace file.
/// </summary>
public sealed record ScriptWorkspaceFileContent
{
    /// <summary>
    /// Normalized logical workspace path.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// File content bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; init; }
}
