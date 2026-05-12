using System.Collections;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Guida.Scripting.Engines;

internal sealed class ScriptApiProjection
{
    private const string JsonContentType = "application/json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ScriptHostContext _hostContext;
    private readonly Dictionary<string, string> _claimedQueueItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _queueNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _registeredStrategies = new(StringComparer.Ordinal);

    public ScriptApiProjection(ScriptHostContext hostContext)
    {
        _hostContext = hostContext ?? ScriptHostContext.Empty;
    }

    public StoreApi Store => new(this);

    public QueueApi Queue => new(this);

    public HttpApi Http => new(this);

    public WorkspaceApi Workspace => new(this);

    public SearchApi Search => new(this);

    public WorkersApi Workers => new(this);

    public WorkerApi Worker => new(this);

    public WorkflowApi Workflow => new(this);

    public WorkflowsApi Workflows => new(this);

    private TCapability Require<TCapability>()
        where TCapability : class, IScriptHostCapability
    {
        if (_hostContext.TryGetCapability<TCapability>(out var capability) && capability is not null)
        {
            return capability;
        }

        throw new ScriptApiProjectionException(ScriptCapabilityUnavailable.For<TCapability>().Message);
    }

    private static void EnsureSuccess(ScriptStoreResult result)
    {
        if (!result.Success)
        {
            throw new ScriptApiProjectionException(result.Error?.Message ?? "Store operation failed.");
        }
    }

    private static void EnsureSuccess(ScriptQueueResult result)
    {
        if (!result.Success)
        {
            throw new ScriptApiProjectionException(result.Error?.Message ?? "Queue operation failed.");
        }
    }

    private static T EnsureSuccess<T>(ScriptStoreResult<T> result)
    {
        if (!result.Success)
        {
            throw new ScriptApiProjectionException(result.Error?.Message ?? "Store operation failed.");
        }

        return result.Value!;
    }

    private static T EnsureSuccess<T>(ScriptQueueResult<T> result)
    {
        if (!result.Success)
        {
            throw new ScriptApiProjectionException(result.Error?.Message ?? "Queue operation failed.");
        }

        return result.Value!;
    }

    private static T EnsureSuccess<T>(ScriptHttpResult<T> result)
    {
        if (!result.Success)
        {
            throw new ScriptApiProjectionException(result.Error?.Message ?? "HTTP operation failed.");
        }

        return result.Value!;
    }

    private static T EnsureSuccess<T>(ScriptSearchResult<T> result)
    {
        if (!result.Success)
        {
            throw new ScriptApiProjectionException(result.Error?.Message ?? "Search operation failed.");
        }

        return result.Value!;
    }

    private static T EnsureSuccess<T>(ScriptWorkspaceResult<T> result)
    {
        if (!result.Success)
        {
            if (result.Error?.Code == ScriptWorkspaceErrorCode.NotFound)
            {
                return default!;
            }

            throw new ScriptApiProjectionException(result.Error?.Message ?? "Workspace operation failed.");
        }

        return result.Value!;
    }

    private static void EnsureSuccess(ScriptWorkspaceResult result)
    {
        if (!result.Success)
        {
            throw new ScriptApiProjectionException(result.Error?.Message ?? "Workspace operation failed.");
        }
    }

    private static T EnsureSuccess<T>(ScriptWorkerResult<T> result)
    {
        if (!result.Success)
        {
            throw new ScriptApiProjectionException(result.Error?.Message ?? "Worker operation failed.");
        }

        return result.Value!;
    }

    private static T EnsureSuccess<T>(ScriptWorkflowLedgerResult<T> result)
    {
        if (!result.Success)
        {
            if (result.Error?.Code == ScriptWorkflowLedgerErrorCode.NotFound)
            {
                return default!;
            }

            throw new ScriptApiProjectionException(result.Error?.Message ?? "Workflow ledger operation failed.");
        }

        return result.Value!;
    }

    private static T EnsureSuccess<T>(ScriptWorkflowBridgeResult<T> result)
    {
        if (!result.Success)
        {
            throw new ScriptApiProjectionException(result.Error?.Message ?? "Workflow bridge operation failed.");
        }

        return result.Value!;
    }

    private static T EnsureSuccess<T>(ScriptWorkflowWorkspaceResult<T> result)
    {
        if (!result.Success)
        {
            throw new ScriptApiProjectionException(result.Error?.Message ?? "Workflow workspace operation failed.");
        }

        return result.Value!;
    }

    private static string StoreKey(string collection, string key) =>
        $"{NormalizeRequired(collection, nameof(collection))}/{NormalizeRequired(key, nameof(key))}";

    private static string StorePrefix(string collection) =>
        $"{NormalizeRequired(collection, nameof(collection))}/";

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ScriptApiProjectionException($"{parameterName} is required.");
        }

        return value.Trim();
    }

    private static object? DeserializeJson(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return null;
        }

        using var document = JsonDocument.Parse(bytes);
        return ToScriptValue(document.RootElement);
    }

    private static byte[] SerializeJson(object? value) =>
        JsonSerializer.SerializeToUtf8Bytes(NormalizeValue(value), JsonOptions);

    private static object? NormalizeValue(object? value)
    {
        if (value is null ||
            value is string ||
            value is bool ||
            value is int ||
            value is long ||
            value is double ||
            value is decimal)
        {
            return value;
        }

        if (value is float single)
        {
            return (double)single;
        }

        if (value is JsonElement element)
        {
            return ToScriptValue(element);
        }

        if (value is IDictionary<string, object?> typedDictionary)
        {
            return typedDictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizeValue(pair.Value),
                StringComparer.Ordinal);
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizeValue(pair.Value),
                StringComparer.Ordinal);
        }

        if (value is IDictionary dictionary)
        {
            var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
            {
                normalized[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] =
                    NormalizeValue(entry.Value);
            }

            return normalized;
        }

        if (value is IEnumerable enumerable and not string)
        {
            var normalized = new List<object?>();
            foreach (var item in enumerable)
            {
                normalized.Add(NormalizeValue(item));
            }

            return normalized;
        }

        return value;
    }

    private static object? ToScriptValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ToScriptValue(property.Value),
                StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ToScriptValue).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? GetString(object? options, string name)
    {
        var value = GetOption(options, name);
        return value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static int? GetInt(object? options, string name)
    {
        var value = GetOption(options, name);
        return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static bool? GetBool(object? options, string name)
    {
        var value = GetOption(options, name);
        return value is null ? null : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    private static object? GetOption(object? options, string name)
    {
        options = NormalizeValue(options);
        if (options is IReadOnlyDictionary<string, object?> dictionary &&
            dictionary.TryGetValue(name, out var value))
        {
            return value;
        }

        return null;
    }

    private static string ToIso(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string? ToIsoOrNull(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> StoreDoc(string collection, ScriptStoreValue value)
    {
        var prefix = StorePrefix(collection);
        var key = value.Key.StartsWith(prefix, StringComparison.Ordinal)
            ? value.Key[prefix.Length..]
            : value.Key;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["key"] = key,
            ["data"] = DeserializeJson(value.Content),
            ["createdAt"] = ToIso(value.CreatedAt),
            ["updatedAt"] = ToIso(value.UpdatedAt)
        };
    }

    private static Dictionary<string, object?> QueueItem(ScriptQueueItem item)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = item.Id,
            ["data"] = DeserializeJson(item.Payload),
            ["priority"] = 0,
            ["groupKey"] = null,
            ["attempts"] = item.AttemptCount,
            ["maxRetries"] = 0,
            ["enqueuedAt"] = ToIso(item.EnqueuedAt),
            ["lastAttemptAt"] = ToIsoOrNull(item.ClaimedAt),
            ["lastError"] = null
        };
    }

    private static Dictionary<string, object?> WorkspaceEntry(ScriptWorkspaceEntry entry) =>
        new(StringComparer.Ordinal)
        {
            ["path"] = entry.Path,
            ["name"] = entry.Name,
            ["kind"] = entry.Kind == ScriptWorkspaceEntryKind.Directory ? "directory" : "file",
            ["length"] = entry.Length,
            ["lastModifiedAt"] = ToIsoOrNull(entry.LastModifiedAt)
        };

    private static Dictionary<string, object?> SearchItem(ScriptSearchItem item) =>
        new(StringComparer.Ordinal)
        {
            ["id"] = item.Id,
            ["title"] = item.Title,
            ["summary"] = item.Summary,
            ["uri"] = item.Uri?.ToString(),
            ["score"] = item.Score,
            ["contentType"] = item.ContentType,
            ["sourceName"] = item.SourceName
        };

    private static Dictionary<string, object?> WorkerJob(ScriptWorkerJob job) =>
        new(StringComparer.Ordinal)
        {
            ["id"] = job.Id,
            ["workerName"] = job.WorkerName,
            ["status"] = job.Status.ToString(),
            ["payload"] = DeserializeJson(job.Payload),
            ["contentType"] = job.ContentType,
            ["correlationId"] = job.CorrelationId,
            ["sourceQueueName"] = job.SourceQueueName,
            ["sourceQueueItemId"] = job.SourceQueueItemId,
            ["createdAt"] = ToIso(job.CreatedAt),
            ["startedAt"] = ToIsoOrNull(job.StartedAt),
            ["endedAt"] = ToIsoOrNull(job.EndedAt),
            ["taskId"] = job.TaskId,
            ["error"] = job.Error,
            ["returnValues"] = job.ReturnValues.ToArray()
        };

    private static object? PublicModel(object? value)
    {
        if (value is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return ToScriptValue(document.RootElement);
    }

    public sealed class StoreApi
    {
        private readonly ScriptApiProjection _projection;

        internal StoreApi(ScriptApiProjection projection) => _projection = projection;

        public async Task<object?> Put(string collection, string key, object? data)
        {
            var store = _projection.Require<IScriptStore>();
            var result = await store.SetAsync(
                StoreKey(collection, key),
                SerializeJson(data),
                new ScriptStoreSetOptions { ContentType = JsonContentType }).ConfigureAwait(false);

            EnsureSuccess(result);
            return null;
        }

        public async Task<object?> Get(string collection, string key)
        {
            var store = _projection.Require<IScriptStore>();
            var result = await store.GetAsync(StoreKey(collection, key)).ConfigureAwait(false);
            if (!result.Success && result.Error?.Code == ScriptStoreErrorCode.NotFound)
            {
                return null;
            }

            return StoreDoc(collection, EnsureSuccess(result));
        }

        public async Task<object?> List(string collection, object? options = null)
        {
            var store = _projection.Require<IScriptStore>();
            var prefix = StorePrefix(collection);
            var entries = EnsureSuccess(await store.ListAsync(prefix).ConfigureAwait(false));
            var offset = Math.Max(0, GetInt(options, "offset") ?? 0);
            var limit = GetInt(options, "limit") ?? int.MaxValue;
            var documents = new List<object?>();

            foreach (var entry in entries.Skip(offset).Take(limit))
            {
                var value = EnsureSuccess(await store.GetAsync(entry.Key).ConfigureAwait(false));
                documents.Add(StoreDoc(collection, value));
            }

            return documents.ToArray();
        }

        public async Task<object?> Search(string collection, string query, object? options = null)
        {
            var documents = ((object?[])(await List(collection, options).ConfigureAwait(false))!);
            if (string.IsNullOrEmpty(query))
            {
                return documents;
            }

            return documents
                .Where(document => JsonSerializer.Serialize(document, JsonOptions).Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        public async Task<bool> Delete(string collection, string key)
        {
            var store = _projection.Require<IScriptStore>();
            var fullKey = StoreKey(collection, key);
            var current = await store.GetAsync(fullKey).ConfigureAwait(false);
            if (!current.Success && current.Error?.Code == ScriptStoreErrorCode.NotFound)
            {
                return false;
            }

            EnsureSuccess(current);
            EnsureSuccess(await store.DeleteAsync(fullKey).ConfigureAwait(false));
            return true;
        }

        public async Task<int> Count(string collection)
        {
            var store = _projection.Require<IScriptStore>();
            var entries = EnsureSuccess(await store.ListAsync(StorePrefix(collection)).ConfigureAwait(false));
            return entries.Count;
        }

        public async Task<object?> Clear(string collection)
        {
            var store = _projection.Require<IScriptStore>();
            var entries = EnsureSuccess(await store.ListAsync(StorePrefix(collection)).ConfigureAwait(false));
            foreach (var entry in entries)
            {
                EnsureSuccess(await store.DeleteAsync(entry.Key).ConfigureAwait(false));
            }

            return null;
        }

        public async Task<string[]> Collections()
        {
            var store = _projection.Require<IScriptStore>();
            var entries = EnsureSuccess(await store.ListAsync().ConfigureAwait(false));
            return entries
                .Select(entry => entry.Key.Split('/', 2)[0])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
    }

    public sealed class QueueApi
    {
        private readonly ScriptApiProjection _projection;

        internal QueueApi(ScriptApiProjection projection) => _projection = projection;

        public async Task<object?> Enqueue(string name, object? data, object? options = null)
        {
            var queue = _projection.Require<IScriptQueue>();
            _projection._queueNames.Add(name);
            var result = await queue.EnqueueAsync(
                name,
                SerializeJson(data),
                new ScriptQueueEnqueueOptions
                {
                    ContentType = JsonContentType,
                    ItemId = GetString(options, "id") ?? GetString(options, "itemId")
                }).ConfigureAwait(false);

            EnsureSuccess(result);
            return null;
        }

        public async Task<object?> Dequeue(string name, object? options = null)
        {
            var queue = _projection.Require<IScriptQueue>();
            _projection._queueNames.Add(name);
            var strategy = GetString(options, "strategy");
            var result = EnsureSuccess(await queue.ClaimAsync(
                name,
                new ScriptQueueClaimOptions
                {
                    MaxItemCount = 1,
                    StrategyName = strategy
                }).ConfigureAwait(false));
            var item = result.FirstOrDefault();
            if (item is null)
            {
                return null;
            }

            _projection._claimedQueueItems[item.Id] = name;
            return QueueItem(item);
        }

        public async Task<object?> Commit(string checkoutId)
        {
            var queue = _projection.Require<IScriptQueue>();
            if (!_projection._claimedQueueItems.TryGetValue(checkoutId, out var queueName))
            {
                throw new ScriptApiProjectionException($"Queue checkout '{checkoutId}' is not known to this script task.");
            }

            EnsureSuccess(await queue.CompleteAsync(queueName, checkoutId).ConfigureAwait(false));
            _projection._claimedQueueItems.Remove(checkoutId);
            return null;
        }

        public async Task<bool> Abort(string checkoutId, string? error = null)
        {
            var queue = _projection.Require<IScriptQueue>();
            if (!_projection._claimedQueueItems.TryGetValue(checkoutId, out var queueName))
            {
                throw new ScriptApiProjectionException($"Queue checkout '{checkoutId}' is not known to this script task.");
            }

            EnsureSuccess(await queue.AbandonAsync(queueName, checkoutId).ConfigureAwait(false));
            _projection._claimedQueueItems.Remove(checkoutId);
            return false;
        }

        public async Task<object?> Peek(string name)
        {
            var queue = _projection.Require<IScriptQueue>();
            _projection._queueNames.Add(name);
            var items = EnsureSuccess(await queue.ListAsync(name).ConfigureAwait(false));
            var item = items.FirstOrDefault();
            return item is null ? null : QueueItem(item);
        }

        public async Task<int> Count(string name)
        {
            var queue = _projection.Require<IScriptQueue>();
            _projection._queueNames.Add(name);
            return EnsureSuccess(await queue.ListAsync(name).ConfigureAwait(false)).Count;
        }

        public async Task<object?> Clear(string name)
        {
            var queue = _projection.Require<IScriptQueue>();
            _projection._queueNames.Add(name);
            var items = EnsureSuccess(await queue.ListAsync(name).ConfigureAwait(false));
            foreach (var item in items)
            {
                EnsureSuccess(await queue.CompleteAsync(name, item.Id).ConfigureAwait(false));
            }

            return null;
        }

        public async Task<object?> List(string name, object? options = null)
        {
            var queue = _projection.Require<IScriptQueue>();
            _projection._queueNames.Add(name);
            var items = EnsureSuccess(await queue.ListAsync(name).ConfigureAwait(false));
            var offset = Math.Max(0, GetInt(options, "offset") ?? 0);
            var limit = GetInt(options, "limit") ?? int.MaxValue;
            return items.Skip(offset).Take(limit).Select(QueueItem).ToArray();
        }

        public string[] Queues() =>
            _projection._queueNames.Order(StringComparer.Ordinal).ToArray();

        public object? DeadLetter(string name, object? options = null) =>
            Array.Empty<object?>();

        public object? Retry(string itemId) =>
            throw new ScriptApiProjectionException("g.queue.retry requires a host queue implementation with dead-letter retry support.");

        public async Task<object?> WaitForItem(string name, object? options = null)
        {
            var timeout = TimeSpan.FromMilliseconds(Math.Max(0, GetInt(options, "timeout") ?? 30000));
            var started = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow - started <= timeout)
            {
                var item = await Dequeue(name, options).ConfigureAwait(false);
                if (item is not null)
                {
                    return item;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            return null;
        }

        public object? RegisterStrategy(string name, object? fnOrPath)
        {
            _projection._registeredStrategies.Add(NormalizeRequired(name, nameof(name)));
            return null;
        }
    }

    public sealed class HttpApi
    {
        private readonly ScriptApiProjection _projection;

        internal HttpApi(ScriptApiProjection projection) => _projection = projection;

        public Task<object?> Get(string url, object? options = null) =>
            Request("GET", url, options);

        public Task<object?> Post(string url, object? options = null) =>
            Request("POST", url, options);

        public async Task<object?> Request(string method, string url, object? options = null)
        {
            var client = _projection.Require<IScriptHttpClient>();
            using var request = new HttpRequestMessage(new HttpMethod(NormalizeRequired(method, nameof(method))), url);
            ApplyHeaders(request, GetOption(options, "headers"));
            ApplySecretHeaders(request, GetOption(options, "secretHeaders"));

            var body = GetOption(options, "body");
            if (body is not null)
            {
                var contentType = GetString(options, "contentType") ?? JsonContentType;
                request.Content = new StringContent(
                    body is string text ? text : JsonSerializer.Serialize(NormalizeValue(body), JsonOptions),
                    Encoding.UTF8,
                    contentType);
            }

            using var response = EnsureSuccess(await client.SendAsync(request).ConfigureAwait(false));
            var responseBody = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["status"] = (int)response.StatusCode,
                ["statusText"] = response.ReasonPhrase,
                ["ok"] = response.IsSuccessStatusCode,
                ["body"] = responseBody,
                ["headers"] = response.Headers
                    .Concat(response.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                    .ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase)
            };
        }

        private static void ApplyHeaders(HttpRequestMessage request, object? headers)
        {
            if (NormalizeValue(headers) is not IReadOnlyDictionary<string, object?> dictionary)
            {
                return;
            }

            foreach (var (name, value) in dictionary)
            {
                request.Headers.TryAddWithoutValidation(name, Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private static void ApplySecretHeaders(HttpRequestMessage request, object? secretHeaders)
        {
            if (NormalizeValue(secretHeaders) is not IEnumerable enumerable)
            {
                return;
            }

            var bindings = new List<ScriptHttpSecretHeaderBinding>();
            foreach (var item in enumerable)
            {
                if (NormalizeValue(item) is not IReadOnlyDictionary<string, object?> dictionary)
                {
                    continue;
                }

                var headerName = dictionary.TryGetValue("headerName", out var explicitName)
                    ? Convert.ToString(explicitName, CultureInfo.InvariantCulture)
                    : dictionary.TryGetValue("name", out var name)
                        ? Convert.ToString(name, CultureInfo.InvariantCulture)
                        : null;
                var secretName = dictionary.TryGetValue("secretName", out var explicitSecretName)
                    ? Convert.ToString(explicitSecretName, CultureInfo.InvariantCulture)
                    : dictionary.TryGetValue("secret", out var secret)
                        ? Convert.ToString(secret, CultureInfo.InvariantCulture)
                        : null;

                if (string.IsNullOrWhiteSpace(headerName) || string.IsNullOrWhiteSpace(secretName))
                {
                    continue;
                }

                bindings.Add(new ScriptHttpSecretHeaderBinding
                {
                    HeaderName = headerName,
                    Secret = new ScriptSecretReference { Name = secretName },
                    ValuePrefix = dictionary.TryGetValue("prefix", out var prefix)
                        ? Convert.ToString(prefix, CultureInfo.InvariantCulture) ?? string.Empty
                        : dictionary.TryGetValue("valuePrefix", out var valuePrefix)
                            ? Convert.ToString(valuePrefix, CultureInfo.InvariantCulture) ?? string.Empty
                            : string.Empty,
                    ReplaceExisting = dictionary.TryGetValue("replaceExisting", out var replaceExisting) &&
                        Convert.ToBoolean(replaceExisting, CultureInfo.InvariantCulture)
                });
            }

            if (bindings.Count > 0)
            {
                ScriptHttpSecretHeaderBindings.Set(request, bindings);
            }
        }
    }

    public sealed class WorkspaceApi
    {
        private readonly ScriptApiProjection _projection;

        internal WorkspaceApi(ScriptApiProjection projection) => _projection = projection;

        public async Task<object?> GetEntry(string path)
        {
            var workspace = _projection.Require<IScriptWorkspace>();
            var entry = EnsureSuccess(await workspace.GetEntryAsync(path).ConfigureAwait(false));
            return entry is null ? null : WorkspaceEntry(entry);
        }

        public async Task<object?> List(string path)
        {
            var workspace = _projection.Require<IScriptWorkspace>();
            var entries = EnsureSuccess(await workspace.ListAsync(path).ConfigureAwait(false));
            return entries.Select(WorkspaceEntry).ToArray();
        }

        public async Task<object?> ReadFile(string path)
        {
            var workspace = _projection.Require<IScriptWorkspace>();
            var content = EnsureSuccess(await workspace.ReadFileAsync(path).ConfigureAwait(false));
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = content.Path,
                ["content"] = Encoding.UTF8.GetString(content.Content.Span)
            };
        }

        public async Task<object?> WriteFile(string path, string content, object? options = null)
        {
            var workspace = _projection.Require<IScriptWorkspace>();
            EnsureSuccess(await workspace.WriteFileAsync(
                path,
                Encoding.UTF8.GetBytes(content),
                new ScriptWorkspaceWriteOptions
                {
                    Overwrite = GetBool(options, "overwrite") ?? true,
                    CreateDirectories = GetBool(options, "createDirectories") ?? false
                }).ConfigureAwait(false));
            return null;
        }
    }

    public sealed class SearchApi
    {
        private readonly ScriptApiProjection _projection;

        internal SearchApi(ScriptApiProjection projection) => _projection = projection;

        public async Task<object?> Query(string query, object? options = null)
        {
            var search = _projection.Require<IScriptSearch>();
            var response = EnsureSuccess(await search.SearchAsync(new ScriptSearchRequest
            {
                Query = query,
                Scope = GetString(options, "scope"),
                Limit = GetInt(options, "limit"),
                Offset = Math.Max(0, GetInt(options, "offset") ?? 0)
            }).ConfigureAwait(false));

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["items"] = response.Items.Select(SearchItem).ToArray(),
                ["totalCount"] = response.TotalCount,
                ["elapsedMs"] = response.Elapsed.TotalMilliseconds
            };
        }

        public Task<object?> Search(string query, object? options = null) =>
            Query(query, options);
    }

    public sealed class WorkersApi
    {
        private readonly ScriptApiProjection _projection;

        internal WorkersApi(ScriptApiProjection projection) => _projection = projection;

        public async Task<object?> Start(object? options)
        {
            var worker = _projection.Require<IScriptWorker>();
            var request = new ScriptWorkerRequest
            {
                WorkerName = GetString(options, "workerName") ?? GetString(options, "name") ?? "worker",
                Payload = SerializeJson(GetOption(options, "payload")),
                ContentType = JsonContentType,
                CorrelationId = GetString(options, "correlationId")
            };

            return WorkerJob(EnsureSuccess(await worker.StartAsync(request).ConfigureAwait(false)));
        }

        public object? Stop(string? queueName = null) =>
            throw new ScriptApiProjectionException("g.workers.stop requires host-specific worker-pool management.");

        public object? Pause() =>
            throw new ScriptApiProjectionException("g.workers.pause requires host-specific worker-pool management.");

        public object? Resume() =>
            throw new ScriptApiProjectionException("g.workers.resume requires host-specific worker-pool management.");

        public async Task<object?> Status()
        {
            var worker = _projection.Require<IScriptWorker>();
            var jobs = EnsureSuccess(await worker.ListAsync().ConfigureAwait(false));
            return jobs.Select(WorkerJob).ToArray();
        }
    }

    public sealed class WorkerApi
    {
        private readonly ScriptApiProjection _projection;

        internal WorkerApi(ScriptApiProjection projection)
        {
            _projection = projection;
            Workflow = new WorkerWorkflowApi(projection);
        }

        public WorkerWorkflowApi Workflow { get; }

        public object? GetContext() =>
            throw new ScriptApiProjectionException("g.worker.getContext requires host-specific worker context.");

        public async Task<object?> Start(string workerName, object? payload = null, object? options = null)
        {
            var worker = _projection.Require<IScriptWorker>();
            return WorkerJob(EnsureSuccess(await worker.StartAsync(
                new ScriptWorkerRequest
                {
                    WorkerName = workerName,
                    Payload = SerializeJson(payload),
                    ContentType = JsonContentType,
                    CorrelationId = GetString(options, "correlationId"),
                    SourceQueueName = GetString(options, "sourceQueueName"),
                    SourceQueueItemId = GetString(options, "sourceQueueItemId")
                }).ConfigureAwait(false)));
        }

        public async Task<object?> Get(string jobId)
        {
            var worker = _projection.Require<IScriptWorker>();
            return WorkerJob(EnsureSuccess(await worker.GetAsync(jobId).ConfigureAwait(false)));
        }

        public async Task<object?> Cancel(string jobId)
        {
            var worker = _projection.Require<IScriptWorker>();
            return WorkerJob(EnsureSuccess(await worker.CancelAsync(jobId).ConfigureAwait(false)));
        }

        public async Task<object?> List()
        {
            var worker = _projection.Require<IScriptWorker>();
            return EnsureSuccess(await worker.ListAsync().ConfigureAwait(false)).Select(WorkerJob).ToArray();
        }
    }

    public sealed class WorkflowApi
    {
        private readonly ScriptApiProjection _projection;

        internal WorkflowApi(ScriptApiProjection projection)
        {
            _projection = projection;
            Runs = new WorkflowRunsApi(projection);
            Items = new WorkflowItemsApi(projection);
        }

        public WorkflowRunsApi Runs { get; }

        public WorkflowItemsApi Items { get; }
    }

    public sealed class WorkflowRunsApi
    {
        private readonly ScriptApiProjection _projection;

        internal WorkflowRunsApi(ScriptApiProjection projection) => _projection = projection;

        public async Task<object?> Start(string workflowName, object? options = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.StartRunAsync(workflowName, new ScriptWorkflowRunOptions
            {
                Source = GetString(options, "source"),
                MetadataJson = JsonMetadata(options)
            }).ConfigureAwait(false)));
        }

        public async Task<object?> Get(string runId)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.GetRunAsync(runId).ConfigureAwait(false)));
        }

        public async Task<object?> List(object? filter = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.ListRunsAsync(new ScriptWorkflowRunQuery
            {
                WorkflowName = GetString(filter, "workflowName"),
                Status = GetString(filter, "status"),
                Skip = GetInt(filter, "skip") ?? 0,
                Take = GetInt(filter, "take") ?? 100
            }).ConfigureAwait(false)));
        }

        public async Task<object?> Finish(string runId, object? options = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.FinishRunAsync(runId, new ScriptWorkflowRunFinishOptions
            {
                MetadataJson = JsonMetadata(options)
            }).ConfigureAwait(false)));
        }

        public async Task<object?> Fail(string runId, object? errorOrOptions)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.FailRunAsync(runId, new ScriptWorkflowRunFailOptions
            {
                Error = errorOrOptions is string text ? text : GetString(errorOrOptions, "error"),
                MetadataJson = JsonMetadata(errorOrOptions)
            }).ConfigureAwait(false)));
        }
    }

    public sealed class WorkflowItemsApi
    {
        private readonly ScriptApiProjection _projection;

        internal WorkflowItemsApi(ScriptApiProjection projection) => _projection = projection;

        public async Task<object?> Upsert(object? input)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.UpsertItemAsync(new ScriptWorkflowItemUpsert
            {
                WorkflowName = GetString(input, "workflowName"),
                ItemKey = GetString(input, "itemKey") ?? GetString(input, "key"),
                ItemType = GetString(input, "itemType") ?? GetString(input, "type"),
                RunId = GetString(input, "runId"),
                Stage = GetString(input, "stage"),
                State = GetString(input, "state"),
                Priority = GetInt(input, "priority") ?? 0,
                MaxAttempts = GetInt(input, "maxAttempts"),
                MetadataJson = JsonMetadata(input)
            }).ConfigureAwait(false)));
        }

        public async Task<object?> Get(string workflowName, string itemKey)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.GetItemAsync(workflowName, itemKey).ConfigureAwait(false)));
        }

        public async Task<object?> GetById(string itemId)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.GetItemByIdAsync(itemId).ConfigureAwait(false)));
        }

        public async Task<object?> Query(object? filter = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.QueryItemsAsync(ItemQuery(filter)).ConfigureAwait(false)));
        }

        public async Task<object?> ClaimNext(object? filter = null, object? leaseOptions = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.ClaimNextAsync(ItemQuery(filter), ClaimOptions(leaseOptions)).ConfigureAwait(false)));
        }

        public async Task<object?> SetState(string itemId, object? update)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.SetItemStateAsync(itemId, new ScriptWorkflowStateUpdate
            {
                Stage = GetString(update, "stage"),
                State = GetString(update, "state"),
                Priority = GetInt(update, "priority"),
                LeaseOwner = GetString(update, "leaseOwner"),
                LastError = GetString(update, "lastError"),
                LastErrorType = GetString(update, "lastErrorType"),
                MetadataJson = JsonMetadata(update)
            }).ConfigureAwait(false)));
        }

        public async Task<object?> AppendEvent(string itemId, object? input)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.AppendEventAsync(itemId, new ScriptWorkflowEventAppend
            {
                RunId = GetString(input, "runId"),
                EventType = GetString(input, "eventType") ?? GetString(input, "type"),
                Stage = GetString(input, "stage"),
                State = GetString(input, "state"),
                Message = GetString(input, "message"),
                Error = GetString(input, "error"),
                IdempotencyKey = GetString(input, "idempotencyKey"),
                MetadataJson = JsonMetadata(input)
            }).ConfigureAwait(false)));
        }

        public async Task<object?> GetEvents(string itemId, object? options = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.GetEventsForItemAsync(itemId, new ScriptWorkflowEventQuery
            {
                Skip = GetInt(options, "skip") ?? 0,
                Take = GetInt(options, "take") ?? 200
            }).ConfigureAwait(false)));
        }

        public async Task<object?> AttachArtifact(string itemId, object? input)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.AttachArtifactAsync(itemId, new ScriptWorkflowArtifactAttach
            {
                EventId = GetString(input, "eventId"),
                ArtifactKind = GetString(input, "artifactKind") ?? GetString(input, "kind"),
                ArtifactRef = GetString(input, "artifactRef") ?? GetString(input, "ref"),
                Role = GetString(input, "role"),
                MetadataJson = JsonMetadata(input)
            }).ConfigureAwait(false)));
        }

        public async Task<object?> GetArtifacts(string itemId)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.GetArtifactsForItemAsync(itemId).ConfigureAwait(false)));
        }

        public async Task<object?> Complete(object? itemRef, object? options = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.CompleteItemAsync(ItemId(itemRef), new ScriptWorkflowItemCompleteOptions
            {
                LeaseOwner = GetString(options, "leaseOwner"),
                MetadataJson = JsonMetadata(options)
            }).ConfigureAwait(false)));
        }

        public async Task<object?> Fail(object? itemRef, object? errorOrOptions)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.FailItemAsync(ItemId(itemRef), new ScriptWorkflowItemFailureOptions
            {
                Error = errorOrOptions is string text ? text : GetString(errorOrOptions, "error"),
                ErrorType = GetString(errorOrOptions, "errorType"),
                LeaseOwner = GetString(errorOrOptions, "leaseOwner"),
                MetadataJson = JsonMetadata(errorOrOptions)
            }).ConfigureAwait(false)));
        }

        public async Task<object?> Release(object? itemRef, object? options = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.ReleaseItemAsync(
                ItemId(itemRef),
                GetString(options, "leaseOwner") ?? string.Empty).ConfigureAwait(false)));
        }

        public async Task<object?> Retry(object? itemRef, object? options = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.RetryItemAsync(ItemId(itemRef)).ConfigureAwait(false)));
        }

        public async Task<object?> DeadLetter(object? itemRef, object? reasonOrOptions)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.DeadLetterItemAsync(
                ItemId(itemRef),
                reasonOrOptions is string text ? text : GetString(reasonOrOptions, "reason") ?? string.Empty).ConfigureAwait(false)));
        }

        public async Task<object?> Enqueue(object? input)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            var queue = _projection.Require<IScriptQueue>();
            return PublicModel(EnsureSuccess(await ScriptWorkflowQueueBridge.EnqueueAsync(
                ledger,
                queue,
                new ScriptWorkflowQueueEnqueueRequest
                {
                    QueueName = GetString(input, "queueName"),
                    Payload = SerializeJson(GetOption(input, "payload")),
                    PayloadContentType = JsonContentType,
                    WorkflowName = GetString(GetOption(input, "item"), "workflowName"),
                    ItemKey = GetString(GetOption(input, "item"), "itemKey") ?? GetString(GetOption(input, "item"), "key"),
                    ItemType = GetString(GetOption(input, "item"), "itemType") ?? GetString(GetOption(input, "item"), "type"),
                    RunId = GetString(GetOption(input, "item"), "runId"),
                    Stage = GetString(GetOption(input, "item"), "stage"),
                    State = GetString(GetOption(input, "item"), "state"),
                    Priority = GetInt(GetOption(input, "item"), "priority") ?? 0,
                    MaxAttempts = GetInt(GetOption(input, "item"), "maxAttempts"),
                    MetadataJson = JsonMetadata(GetOption(input, "item")),
                    EventType = GetString(GetOption(input, "event"), "eventType") ?? GetString(GetOption(input, "event"), "type"),
                    EventIdempotencyKey = GetString(GetOption(input, "event"), "idempotencyKey"),
                    EventMetadataJson = JsonMetadata(GetOption(input, "event"))
                }).ConfigureAwait(false)));
        }

        private static ScriptWorkflowItemQuery ItemQuery(object? filter) =>
            new()
            {
                WorkflowName = GetString(filter, "workflowName"),
                RunId = GetString(filter, "runId"),
                Stage = GetString(filter, "stage"),
                State = GetString(filter, "state"),
                Skip = GetInt(filter, "skip") ?? 0,
                Take = GetInt(filter, "take") ?? 100
            };

        private static ScriptWorkflowClaimOptions ClaimOptions(object? options) =>
            new()
            {
                LeaseOwner = GetString(options, "leaseOwner"),
                LeaseDuration = TimeSpan.FromMilliseconds(GetInt(options, "leaseDurationMs") ?? 300000),
                Take = GetInt(options, "take") ?? 1
            };

        private static string ItemId(object? itemRef)
        {
            if (itemRef is string text)
            {
                return text;
            }

            return GetString(itemRef, "id")
                ?? throw new ScriptApiProjectionException("Workflow item id is required.");
        }
    }

    public sealed class WorkflowsApi
    {
        private readonly ScriptApiProjection _projection;

        internal WorkflowsApi(ScriptApiProjection projection) => _projection = projection;

        public async Task<object?> GetActive()
        {
            var manager = _projection.Require<IScriptWorkflowWorkspaceManager>();
            var workspace = _projection.Require<IScriptWorkspace>();
            var model = EnsureSuccess(await manager.ListAsync(workspace).ConfigureAwait(false));
            var active = model.Workflows.FirstOrDefault(workflow => workflow.IsActive);
            return active is null
                ? null
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = active.Name,
                    ["path"] = active.Path
                };
        }

        public async Task<object?> List()
        {
            var manager = _projection.Require<IScriptWorkflowWorkspaceManager>();
            var workspace = _projection.Require<IScriptWorkspace>();
            var model = EnsureSuccess(await manager.ListAsync(workspace).ConfigureAwait(false));
            return PublicModel(model.Workflows);
        }

        public async Task<object?> Switch(string name)
        {
            var manager = _projection.Require<IScriptWorkflowWorkspaceManager>();
            var workspace = _projection.Require<IScriptWorkspace>();
            EnsureSuccess(await manager.ActivateAsync(workspace, name).ConfigureAwait(false));
            return null;
        }
    }

    public sealed class WorkerWorkflowApi
    {
        private readonly ScriptApiProjection _projection;

        internal WorkerWorkflowApi(ScriptApiProjection projection) => _projection = projection;

        public object? GetContext()
        {
            var provider = _projection.Require<IScriptWorkflowWorkerContextProvider>();
            if (provider.Current is null)
            {
                throw new ScriptApiProjectionException("No current workflow worker context is available.");
            }

            return PublicModel(provider.Current);
        }

        public async Task<object?> GetItem()
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            var context = CurrentContext();
            return PublicModel(EnsureSuccess(await ledger.GetItemByIdAsync(context.ItemId).ConfigureAwait(false)));
        }

        public async Task<object?> Claim(object? options = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.ClaimItemAsync(CurrentContext().ItemId, ClaimOptions(options)).ConfigureAwait(false)));
        }

        public async Task<object?> Complete(object? options = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.CompleteItemAsync(CurrentContext().ItemId, new ScriptWorkflowItemCompleteOptions
            {
                LeaseOwner = GetString(options, "leaseOwner") ?? CurrentContext().LeaseOwner,
                MetadataJson = JsonMetadata(options)
            }).ConfigureAwait(false)));
        }

        public async Task<object?> Fail(object? errorOrOptions)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.FailItemAsync(CurrentContext().ItemId, new ScriptWorkflowItemFailureOptions
            {
                Error = errorOrOptions is string text ? text : GetString(errorOrOptions, "error"),
                ErrorType = GetString(errorOrOptions, "errorType"),
                LeaseOwner = GetString(errorOrOptions, "leaseOwner") ?? CurrentContext().LeaseOwner,
                MetadataJson = JsonMetadata(errorOrOptions)
            }).ConfigureAwait(false)));
        }

        public async Task<object?> Release(object? options = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.ReleaseItemAsync(
                CurrentContext().ItemId,
                GetString(options, "leaseOwner") ?? CurrentContext().LeaseOwner).ConfigureAwait(false)));
        }

        public async Task<object?> Retry(object? options = null)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.RetryItemAsync(CurrentContext().ItemId).ConfigureAwait(false)));
        }

        public async Task<object?> DeadLetter(object? reasonOrOptions)
        {
            var ledger = _projection.Require<IScriptWorkflowLedger>();
            return PublicModel(EnsureSuccess(await ledger.DeadLetterItemAsync(
                CurrentContext().ItemId,
                reasonOrOptions is string text ? text : GetString(reasonOrOptions, "reason") ?? string.Empty).ConfigureAwait(false)));
        }

        private ScriptWorkflowWorkerContext CurrentContext()
        {
            var provider = _projection.Require<IScriptWorkflowWorkerContextProvider>();
            return provider.Current ?? throw new ScriptApiProjectionException("No current workflow worker context is available.");
        }

        private static ScriptWorkflowClaimOptions ClaimOptions(object? options) =>
            new()
            {
                LeaseOwner = GetString(options, "leaseOwner"),
                LeaseDuration = TimeSpan.FromMilliseconds(GetInt(options, "leaseDurationMs") ?? 300000),
                Take = 1
            };
    }

    private static string? JsonMetadata(object? value)
    {
        var metadata = GetOption(value, "metadata");
        return metadata is null ? null : JsonSerializer.Serialize(NormalizeValue(metadata), JsonOptions);
    }
}

internal sealed class ScriptApiProjectionException : InvalidOperationException
{
    public ScriptApiProjectionException(string message)
        : base(message)
    {
    }
}
