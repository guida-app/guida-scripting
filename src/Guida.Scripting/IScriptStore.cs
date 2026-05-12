namespace Guida.Scripting;

/// <summary>
/// Host-mediated key-value store available to script engines and API adapters.
/// </summary>
public interface IScriptStore : IScriptHostCapability
{
    /// <summary>
    /// Gets one stored value by key.
    /// </summary>
    Task<ScriptStoreResult<ScriptStoreValue>> GetAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets one stored value by key.
    /// </summary>
    Task<ScriptStoreResult> SetAsync(
        string key,
        ReadOnlyMemory<byte> content,
        ScriptStoreSetOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one stored value by key.
    /// </summary>
    Task<ScriptStoreResult> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists stored values, optionally filtered by key prefix.
    /// </summary>
    Task<ScriptStoreResult<IReadOnlyList<ScriptStoreEntry>>> ListAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default);
}
