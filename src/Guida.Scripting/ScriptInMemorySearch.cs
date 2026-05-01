namespace Guida.Scripting;

/// <summary>
/// In-memory search capability for tests, samples, and simple hosts.
/// </summary>
public sealed class ScriptInMemorySearch : IScriptSearch
{
    private readonly IReadOnlyList<SearchDocument> _documents;

    /// <summary>
    /// Creates an in-memory search provider from explicit documents.
    /// </summary>
    public ScriptInMemorySearch(IEnumerable<ScriptInMemorySearchDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        _documents = documents
            .Select(document =>
            {
                ArgumentNullException.ThrowIfNull(document);
                return new SearchDocument(
                    document.Item,
                    document.SearchText ?? string.Empty);
            })
            .ToArray();
    }

    /// <inheritdoc />
    public Task<ScriptSearchResult<ScriptSearchResponse>> SearchAsync(
        ScriptSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startedAt = DateTimeOffset.UtcNow;
        var validation = ValidateRequest(request);
        if (!validation.Success)
        {
            return Task.FromResult(ScriptSearchResult<ScriptSearchResponse>.Failed(validation.Error!));
        }

        var query = request.Query.Trim();
        var matches = _documents
            .Where(document => ScopeMatches(request.Scope, document.Item.SourceName) &&
                TextMatches(query, document))
            .Select(document => WithScore(document.Item, CalculateScore(query, document)))
            .OrderByDescending(item => item.Score ?? 0)
            .ThenBy(item => item.Title, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var offset = Math.Max(request.Offset, 0);
        var limit = request.Limit is > 0 ? request.Limit.Value : matches.Length;
        var items = matches
            .Skip(offset)
            .Take(limit)
            .ToArray();
        var response = new ScriptSearchResponse
        {
            Items = items,
            TotalCount = matches.Length,
            Elapsed = DateTimeOffset.UtcNow - startedAt
        };

        return Task.FromResult(ScriptSearchResult<ScriptSearchResponse>.Succeeded(response));
    }

    private static ScriptSearchResult ValidateRequest(ScriptSearchRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Query))
        {
            return ScriptSearchResult.Failed(
                Failed(
                    ScriptSearchErrorCode.InvalidQuery,
                    request?.Query ?? string.Empty,
                    request?.Scope,
                    "Search query cannot be empty."));
        }

        if (request.Scope is not null && string.IsNullOrWhiteSpace(request.Scope))
        {
            return ScriptSearchResult.Failed(
                Failed(
                    ScriptSearchErrorCode.InvalidScope,
                    request.Query,
                    request.Scope,
                    "Search scope cannot be empty."));
        }

        return ScriptSearchResult.Succeeded();
    }

    private static bool ScopeMatches(string? scope, string? sourceName) =>
        scope is null || string.Equals(scope, sourceName, StringComparison.Ordinal);

    private static bool TextMatches(string query, SearchDocument document) =>
        Contains(document.Item.Id, query) ||
        Contains(document.Item.Title, query) ||
        Contains(document.Item.Summary, query) ||
        Contains(document.SearchText, query);

    private static double CalculateScore(string query, SearchDocument document)
    {
        if (Contains(document.Item.Title, query))
        {
            return 3;
        }

        if (Contains(document.Item.Summary, query))
        {
            return 2;
        }

        if (Contains(document.SearchText, query))
        {
            return 1;
        }

        return 0.5;
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static ScriptSearchItem WithScore(ScriptSearchItem item, double score) =>
        item with { Score = item.Score ?? score };

    private static ScriptSearchError Failed(
        ScriptSearchErrorCode code,
        string query,
        string? scope,
        string message) =>
        new(code, query, scope, message);

    private sealed record SearchDocument(ScriptSearchItem Item, string SearchText);
}
