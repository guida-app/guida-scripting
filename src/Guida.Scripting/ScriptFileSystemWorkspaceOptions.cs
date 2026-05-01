namespace Guida.Scripting;

/// <summary>
/// Options for a local filesystem-backed script workspace.
/// </summary>
public sealed record ScriptFileSystemWorkspaceOptions
{
    /// <summary>
    /// Whether write operations are denied.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Whether symlinks and other reparse points are denied.
    /// </summary>
    public bool DenyReparsePoints { get; init; } = true;

    /// <summary>
    /// Whether the workspace root should be created when it does not exist.
    /// </summary>
    public bool CreateRoot { get; init; }
}
