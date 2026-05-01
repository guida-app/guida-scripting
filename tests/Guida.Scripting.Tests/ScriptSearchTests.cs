using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptSearchTests
{
    [Fact]
    public void Search_can_be_registered_and_retrieved_from_host_context()
    {
        var search = new ScriptInMemorySearch([]);
        var context = ScriptHostContext.Empty.WithCapability<IScriptSearch>(search);

        Assert.True(context.TryGetCapability<IScriptSearch>(out var found));
        Assert.Same(search, found);
        Assert.Same(search, context.GetCapability<IScriptSearch>());
    }

    [Fact]
    public void Missing_search_uses_capability_unavailable_reporting()
    {
        var unavailable = ScriptCapabilityUnavailable.For<IScriptSearch>();
        var result = unavailable.ToExecutionResult();

        Assert.False(ScriptHostContext.Empty.TryGetCapability<IScriptSearch>(out _));
        Assert.Equal(typeof(IScriptSearch).FullName, unavailable.CapabilityName);
        Assert.False(result.Success);
        Assert.Contains(nameof(IScriptSearch), result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Search_returns_invalid_query_for_empty_queries(string query)
    {
        var search = new ScriptInMemorySearch([]);

        var result = await search.SearchAsync(new ScriptSearchRequest { Query = query });

        Assert.False(result.Success);
        Assert.Equal(ScriptSearchErrorCode.InvalidQuery, result.Error?.Code);
        Assert.Equal(query, result.Error?.Query);
    }

    [Fact]
    public async Task Search_returns_invalid_scope_for_empty_scope()
    {
        var search = new ScriptInMemorySearch([]);

        var result = await search.SearchAsync(new ScriptSearchRequest
        {
            Query = "alpha",
            Scope = " "
        });

        Assert.False(result.Success);
        Assert.Equal(ScriptSearchErrorCode.InvalidScope, result.Error?.Code);
        Assert.Equal(" ", result.Error?.Scope);
    }

    [Fact]
    public async Task Search_returns_matching_items()
    {
        var search = CreateSearch();

        var result = await search.SearchAsync(new ScriptSearchRequest { Query = "alpha" });

        Assert.True(result.Success);
        Assert.Equal(2, result.Value?.TotalCount);
        Assert.Equal(["alpha", "notes"], result.Value!.Items.Select(item => item.Id).ToArray());
        Assert.True(result.Value.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Search_matching_is_case_insensitive()
    {
        var search = CreateSearch();

        var result = await search.SearchAsync(new ScriptSearchRequest { Query = "ALPHA" });

        Assert.True(result.Success);
        Assert.Equal(["alpha", "notes"], result.Value!.Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task Search_scope_filters_by_source_name()
    {
        var search = CreateSearch();

        var result = await search.SearchAsync(new ScriptSearchRequest
        {
            Query = "alpha",
            Scope = "workspace"
        });

        Assert.True(result.Success);
        Assert.Equal(["alpha"], result.Value!.Items.Select(item => item.Id).ToArray());
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task Search_applies_offset_and_limit()
    {
        var search = CreateSearch();

        var result = await search.SearchAsync(new ScriptSearchRequest
        {
            Query = "guide",
            Offset = 1,
            Limit = 2
        });

        Assert.True(result.Success);
        Assert.Equal(3, result.Value?.TotalCount);
        Assert.Equal(["beta", "gamma"], result.Value!.Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task Search_results_are_deterministic()
    {
        var search = new ScriptInMemorySearch(
        [
            Document("b", "Same", "needle", "workspace"),
            Document("a", "Same", "needle", "workspace"),
            Document("c", "Same", "needle", "workspace")
        ]);

        var result = await search.SearchAsync(new ScriptSearchRequest { Query = "needle" });

        Assert.True(result.Success);
        Assert.Equal(["a", "b", "c"], result.Value!.Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task Search_empty_result_succeeds()
    {
        var search = CreateSearch();

        var result = await search.SearchAsync(new ScriptSearchRequest { Query = "missing" });

        Assert.True(result.Success);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task Search_operations_observe_cancellation()
    {
        var search = CreateSearch();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            search.SearchAsync(
                new ScriptSearchRequest { Query = "alpha" },
                cancellationTokenSource.Token));
    }

    [Fact]
    public void Search_error_converts_to_failed_execution_result()
    {
        var error = new ScriptSearchError(
            ScriptSearchErrorCode.AccessDenied,
            "alpha",
            "workspace",
            "Search access was denied.");

        var result = error.ToExecutionResult();

        Assert.False(result.Success);
        Assert.Equal(error.Message, result.Error);
    }

    private static ScriptInMemorySearch CreateSearch() =>
        new(
        [
            Document("alpha", "Alpha Guide", "Workspace alpha document", "workspace"),
            Document("notes", "Notes", "Mentions alpha in the body", "store"),
            Document("beta", "Beta Guide", "Guide body", "workspace"),
            Document("gamma", "Gamma Guide", "Guide body", "store")
        ]);

    private static ScriptInMemorySearchDocument Document(
        string id,
        string title,
        string searchText,
        string sourceName) =>
        new()
        {
            Item = new ScriptSearchItem
            {
                Id = id,
                Title = title,
                Summary = searchText,
                SourceName = sourceName,
                ContentType = "text/plain"
            },
            SearchText = searchText
        };
}
