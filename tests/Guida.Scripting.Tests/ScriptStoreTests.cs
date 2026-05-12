using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptStoreTests
{
    [Fact]
    public void Store_can_be_registered_and_retrieved_from_host_context()
    {
        var store = new ScriptInMemoryStore();
        var context = ScriptHostContext.Empty.WithCapability<IScriptStore>(store);

        Assert.True(context.TryGetCapability<IScriptStore>(out var found));
        Assert.Same(store, found);
        Assert.Same(store, context.GetCapability<IScriptStore>());
    }

    [Fact]
    public void Missing_store_uses_capability_unavailable_reporting()
    {
        var unavailable = ScriptCapabilityUnavailable.For<IScriptStore>();
        var result = unavailable.ToExecutionResult();

        Assert.False(ScriptHostContext.Empty.TryGetCapability<IScriptStore>(out _));
        Assert.Equal(typeof(IScriptStore).FullName, unavailable.CapabilityName);
        Assert.False(result.Success);
        Assert.Contains(nameof(IScriptStore), result.Error);
    }

    [Fact]
    public async Task Store_sets_and_gets_content()
    {
        var store = new ScriptInMemoryStore();
        var content = new byte[] { 1, 2, 3 };

        var set = await store.SetAsync(
            "items/one",
            content,
            new ScriptStoreSetOptions { ContentType = "application/octet-stream" });
        var get = await store.GetAsync("items/one");

        Assert.True(set.Success);
        Assert.True(get.Success);
        Assert.Equal("items/one", get.Value?.Key);
        Assert.Equal(content, get.Value?.Content.ToArray());
        Assert.Equal("application/octet-stream", get.Value?.ContentType);
        Assert.NotNull(get.Value?.CreatedAt);
        Assert.NotNull(get.Value?.UpdatedAt);
    }

    [Fact]
    public async Task Store_defensively_copies_content()
    {
        var store = new ScriptInMemoryStore();
        var content = new byte[] { 1, 2, 3 };

        await store.SetAsync("item", content);
        content[0] = 9;

        var first = await store.GetAsync("item");
        first.Value!.Content.ToArray()[1] = 9;
        var second = await store.GetAsync("item");

        Assert.Equal(new byte[] { 1, 2, 3 }, first.Value.Content.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, second.Value?.Content.ToArray());
    }

    [Fact]
    public async Task Store_returns_not_found_for_missing_key()
    {
        var store = new ScriptInMemoryStore();

        var result = await store.GetAsync("missing");

        Assert.False(result.Success);
        Assert.Equal(ScriptStoreErrorCode.NotFound, result.Error?.Code);
        Assert.Equal("missing", result.Error?.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Store_returns_invalid_key_for_empty_keys(string key)
    {
        var store = new ScriptInMemoryStore();

        var get = await store.GetAsync(key);
        var set = await store.SetAsync(key, ReadOnlyMemory<byte>.Empty);
        var delete = await store.DeleteAsync(key);

        Assert.Equal(ScriptStoreErrorCode.InvalidKey, get.Error?.Code);
        Assert.Equal(ScriptStoreErrorCode.InvalidKey, set.Error?.Code);
        Assert.Equal(ScriptStoreErrorCode.InvalidKey, delete.Error?.Code);
    }

    [Fact]
    public async Task Store_returns_already_exists_when_overwrite_is_false()
    {
        var store = new ScriptInMemoryStore();

        await store.SetAsync("item", new byte[] { 1 });
        var result = await store.SetAsync(
            "item",
            new byte[] { 2 },
            new ScriptStoreSetOptions { Overwrite = false });

        Assert.False(result.Success);
        Assert.Equal(ScriptStoreErrorCode.AlreadyExists, result.Error?.Code);
    }

    [Fact]
    public async Task Store_overwrites_existing_content_by_default()
    {
        var store = new ScriptInMemoryStore();

        await store.SetAsync("item", new byte[] { 1 });
        var set = await store.SetAsync("item", new byte[] { 2 });
        var get = await store.GetAsync("item");

        Assert.True(set.Success);
        Assert.Equal(new byte[] { 2 }, get.Value?.Content.ToArray());
    }

    [Fact]
    public async Task Store_delete_removes_existing_key()
    {
        var store = new ScriptInMemoryStore();

        await store.SetAsync("item", new byte[] { 1 });
        var delete = await store.DeleteAsync("item");
        var get = await store.GetAsync("item");

        Assert.True(delete.Success);
        Assert.Equal(ScriptStoreErrorCode.NotFound, get.Error?.Code);
    }

    [Fact]
    public async Task Store_delete_missing_key_succeeds()
    {
        var store = new ScriptInMemoryStore();

        var result = await store.DeleteAsync("missing");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Store_lists_entries_sorted_by_key()
    {
        var store = new ScriptInMemoryStore();
        await store.SetAsync("b", new byte[] { 1, 2 });
        await store.SetAsync("a", new byte[] { 1 });
        await store.SetAsync("c", new byte[] { 1, 2, 3 });

        var result = await store.ListAsync();

        Assert.True(result.Success);
        var entries = result.Value!;
        Assert.Equal(["a", "b", "c"], entries.Select(entry => entry.Key).ToArray());
        Assert.Equal([1L, 2L, 3L], entries.Select(entry => entry.Length).ToArray());
    }

    [Fact]
    public async Task Store_list_filters_by_prefix()
    {
        var store = new ScriptInMemoryStore();
        await store.SetAsync("scripts/one", new byte[] { 1 });
        await store.SetAsync("scripts/two", new byte[] { 2 });
        await store.SetAsync("state/one", new byte[] { 3 });

        var result = await store.ListAsync("scripts/");

        Assert.True(result.Success);
        var entries = result.Value!;
        Assert.Equal(["scripts/one", "scripts/two"], entries.Select(entry => entry.Key).ToArray());
    }

    [Fact]
    public async Task Store_list_returns_invalid_key_for_empty_prefix()
    {
        var store = new ScriptInMemoryStore();

        var result = await store.ListAsync(" ");

        Assert.False(result.Success);
        Assert.Equal(ScriptStoreErrorCode.InvalidKey, result.Error?.Code);
    }

    [Fact]
    public async Task Store_operations_observe_cancellation()
    {
        var store = new ScriptInMemoryStore();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.GetAsync("item", cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.SetAsync("item", ReadOnlyMemory<byte>.Empty, cancellationToken: cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.DeleteAsync("item", cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.ListAsync(cancellationToken: cancellationTokenSource.Token));
    }

    [Fact]
    public void Store_error_converts_to_failed_execution_result()
    {
        var error = new ScriptStoreError(
            ScriptStoreErrorCode.AccessDenied,
            "item",
            "Store access was denied.");

        var result = error.ToExecutionResult();

        Assert.False(result.Success);
        Assert.Equal(error.Message, result.Error);
    }
}
