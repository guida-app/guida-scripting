using System.Collections.ObjectModel;

namespace Guida.Scripting;

/// <summary>
/// In-memory script store for tests, samples, and simple hosts.
/// </summary>
public sealed class ScriptInMemoryStore : IScriptStore
{
    private readonly Dictionary<string, StoreItem> _items;
    private readonly object _gate = new();

    /// <summary>
    /// Creates an empty in-memory script store.
    /// </summary>
    public ScriptInMemoryStore()
    {
        _items = new Dictionary<string, StoreItem>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Creates an in-memory script store from explicit values.
    /// </summary>
    public ScriptInMemoryStore(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        _items = new Dictionary<string, StoreItem>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in values)
        {
            ValidateKey(pair.Key);
            _items[pair.Key] = new StoreItem(
                Copy(pair.Value),
                ContentType: null,
                CreatedAt: now,
                UpdatedAt: now);
        }
    }

    /// <inheritdoc />
    public Task<ScriptStoreResult<ScriptStoreValue>> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateKeyResult(key);
        if (!validation.Success)
        {
            return Task.FromResult(ScriptStoreResult<ScriptStoreValue>.Failed(validation.Error!));
        }

        lock (_gate)
        {
            if (!_items.TryGetValue(key, out var item))
            {
                return Task.FromResult(ScriptStoreResult<ScriptStoreValue>.Failed(
                    Failed(ScriptStoreErrorCode.NotFound, key, $"Store key '{key}' was not found.")));
            }

            return Task.FromResult(ScriptStoreResult<ScriptStoreValue>.Succeeded(new ScriptStoreValue
            {
                Key = key,
                Content = Copy(item.Content),
                ContentType = item.ContentType,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            }));
        }
    }

    /// <inheritdoc />
    public Task<ScriptStoreResult> SetAsync(
        string key,
        ReadOnlyMemory<byte> content,
        ScriptStoreSetOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateKeyResult(key);
        if (!validation.Success)
        {
            return Task.FromResult(validation);
        }

        options ??= new ScriptStoreSetOptions();

        lock (_gate)
        {
            if (!options.Overwrite && _items.ContainsKey(key))
            {
                return Task.FromResult(ScriptStoreResult.Failed(
                    Failed(ScriptStoreErrorCode.AlreadyExists, key, $"Store key '{key}' already exists.")));
            }

            var now = DateTimeOffset.UtcNow;
            var createdAt = _items.TryGetValue(key, out var existing)
                ? existing.CreatedAt
                : now;

            _items[key] = new StoreItem(
                Copy(content),
                options.ContentType,
                createdAt,
                now);
        }

        return Task.FromResult(ScriptStoreResult.Succeeded());
    }

    /// <inheritdoc />
    public Task<ScriptStoreResult> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateKeyResult(key);
        if (!validation.Success)
        {
            return Task.FromResult(validation);
        }

        lock (_gate)
        {
            _items.Remove(key);
        }

        return Task.FromResult(ScriptStoreResult.Succeeded());
    }

    /// <inheritdoc />
    public Task<ScriptStoreResult<IReadOnlyList<ScriptStoreEntry>>> ListAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (prefix is not null && string.IsNullOrWhiteSpace(prefix))
        {
            return Task.FromResult(ScriptStoreResult<IReadOnlyList<ScriptStoreEntry>>.Failed(
                Failed(ScriptStoreErrorCode.InvalidKey, prefix, "Store key prefix cannot be empty.")));
        }

        lock (_gate)
        {
            var entries = _items
                .Where(pair => prefix is null || pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ScriptStoreEntry
                {
                    Key = pair.Key,
                    Length = pair.Value.Content.Length,
                    ContentType = pair.Value.ContentType,
                    CreatedAt = pair.Value.CreatedAt,
                    UpdatedAt = pair.Value.UpdatedAt
                })
                .ToArray();

            return Task.FromResult(ScriptStoreResult<IReadOnlyList<ScriptStoreEntry>>.Succeeded(
                new ReadOnlyCollection<ScriptStoreEntry>(entries)));
        }
    }

    private static ScriptStoreResult ValidateKeyResult(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return ScriptStoreResult.Failed(
                Failed(ScriptStoreErrorCode.InvalidKey, key ?? string.Empty, "Store key cannot be empty."));
        }

        return ScriptStoreResult.Succeeded();
    }

    private static void ValidateKey(string key)
    {
        if (!ValidateKeyResult(key).Success)
        {
            throw new ArgumentException("Store key cannot be empty.", nameof(key));
        }
    }

    private static ScriptStoreError Failed(
        ScriptStoreErrorCode code,
        string key,
        string message) =>
        new(code, key, message);

    private static byte[] Copy(ReadOnlyMemory<byte> content) => content.ToArray();

    private sealed record StoreItem(
        byte[] Content,
        string? ContentType,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
