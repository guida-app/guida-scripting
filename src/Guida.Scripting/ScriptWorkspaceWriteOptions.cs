namespace Guida.Scripting;

/// <summary>
/// Options used when writing a file to a script workspace.
/// </summary>
public sealed record ScriptWorkspaceWriteOptions
{
    /// <summary>
    /// Whether an existing file may be overwritten.
    /// </summary>
    public bool Overwrite { get; init; } = true;

    /// <summary>
    /// Whether missing parent directories may be created by the host.
    /// </summary>
    public bool CreateDirectories { get; init; }
}
