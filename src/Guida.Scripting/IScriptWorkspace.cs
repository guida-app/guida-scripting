namespace Guida.Scripting;

/// <summary>
/// Provides access to a host-owned logical file workspace.
/// </summary>
public interface IScriptWorkspace : IScriptHostCapability
{
    /// <summary>
    /// Whether the workspace denies write operations.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Gets metadata for a logical workspace path.
    /// </summary>
    Task<ScriptWorkspaceResult<ScriptWorkspaceEntry>> GetEntryAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists entries below a logical workspace directory path.
    /// </summary>
    Task<ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>> ListAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads file content from a logical workspace path.
    /// </summary>
    Task<ScriptWorkspaceResult<ScriptWorkspaceFileContent>> ReadFileAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes file content to a logical workspace path.
    /// </summary>
    Task<ScriptWorkspaceResult> WriteFileAsync(
        string path,
        ReadOnlyMemory<byte> content,
        ScriptWorkspaceWriteOptions? options = null,
        CancellationToken cancellationToken = default);
}
