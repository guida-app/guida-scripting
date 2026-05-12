using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Guida.Scripting;
using Guida.Scripting.Engines;

namespace Guida.Scripting.Tests;

public sealed class ScriptApiProjectionTests
{
    [Theory]
    [MemberData(nameof(StoreScripts))]
    public async Task Engines_project_store_api(ScriptLanguage language, string source)
    {
        var hostContext = ScriptHostContext.Empty.WithCapability<IScriptStore>(new ScriptInMemoryStore());
        using var engine = CreateEngine(language, hostContext);

        var result = await ExecuteAsync(engine, language, source, hostContext);

        Assert.True(result.Success, result.Error);
        AssertSingleValue(result, "hello");
    }

    [Theory]
    [MemberData(nameof(QueueScripts))]
    public async Task Engines_project_queue_api(ScriptLanguage language, string source)
    {
        var hostContext = ScriptHostContext.Empty.WithCapability<IScriptQueue>(new ScriptInMemoryQueue());
        using var engine = CreateEngine(language, hostContext);

        var result = await ExecuteAsync(engine, language, source, hostContext);

        Assert.True(result.Success, result.Error);
        AssertSingleValue(result, "payload");
    }

    [Theory]
    [MemberData(nameof(SearchScripts))]
    public async Task Engines_project_search_api(ScriptLanguage language, string source)
    {
        var search = new ScriptInMemorySearch(
        [
            new ScriptInMemorySearchDocument
            {
                Item = new ScriptSearchItem { Id = "one", Title = "Needle result" },
                SearchText = "needle"
            }
        ]);
        var hostContext = ScriptHostContext.Empty.WithCapability<IScriptSearch>(search);
        using var engine = CreateEngine(language, hostContext);

        var result = await ExecuteAsync(engine, language, source, hostContext);

        Assert.True(result.Success, result.Error);
        AssertSingleValue(result, "Needle result");
    }

    [Theory]
    [MemberData(nameof(WorkspaceScripts))]
    public async Task Engines_project_workspace_api(ScriptLanguage language, string source)
    {
        var root = Path.Combine(Path.GetTempPath(), "guida-scripting-projection-" + Guid.NewGuid().ToString("N"));
        var workspace = new ScriptFileSystemWorkspace(root, new ScriptFileSystemWorkspaceOptions { CreateRoot = true });
        var hostContext = ScriptHostContext.Empty.WithCapability<IScriptWorkspace>(workspace);
        using var engine = CreateEngine(language, hostContext);

        try
        {
            var result = await ExecuteAsync(engine, language, source, hostContext);

            Assert.True(result.Success, result.Error);
            AssertSingleValue(result, "workspace");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_capability_failure_is_stable()
    {
        using var engine = CreateEngine(ScriptLanguage.JavaScript, ScriptHostContext.Empty);

        var result = await ExecuteAsync(
            engine,
            ScriptLanguage.JavaScript,
            "async function main() { return await g.store.get('docs', 'one'); }",
            ScriptHostContext.Empty);

        Assert.False(result.Success);
        Assert.Contains(nameof(IScriptStore), result.Error);
        Assert.Contains("unavailable", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Http_projection_preserves_secret_header_binding_boundary()
    {
        var capture = new CapturingHttpClient();
        var secrets = new ScriptInMemorySecretProvider(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["api-token"] = "secret-value"
            });
        var http = new SecretBindingScriptHttpClient(capture, secrets);
        var hostContext = ScriptHostContext.Empty.WithCapability<IScriptHttpClient>(http);
        using var engine = CreateEngine(ScriptLanguage.JavaScript, hostContext);

        var result = await ExecuteAsync(
            engine,
            ScriptLanguage.JavaScript,
            """
            async function main() {
              const response = await g.http.get('https://example.test/', {
                secretHeaders: [{ headerName: 'Authorization', secretName: 'api-token', valuePrefix: 'Bearer ' }]
              });
              return response.status;
            }
            """,
            hostContext);

        Assert.True(result.Success, result.Error);
        AssertSingleValue(result, 200);
        Assert.Equal("Bearer secret-value", Assert.Single(capture.LastRequest!.Headers.GetValues("Authorization")));
        Assert.DoesNotContain(result.ReturnValues, value => value?.ToString()?.Contains("secret-value", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Queue_projection_passes_strategy_name_through_public_claim_options()
    {
        var queue = new CapturingQueue();
        var hostContext = ScriptHostContext.Empty.WithCapability<IScriptQueue>(queue);
        using var engine = CreateEngine(ScriptLanguage.JavaScript, hostContext);

        var result = await ExecuteAsync(
            engine,
            ScriptLanguage.JavaScript,
            """
            async function main() {
              await g.queue.enqueue('jobs', 'payload', {});
              await g.queue.dequeue('jobs', { strategy: 'least-loaded' });
              return 'ok';
            }
            """,
            hostContext);

        Assert.True(result.Success, result.Error);
        Assert.Equal("least-loaded", queue.LastClaimOptions?.StrategyName);
    }

    [Theory]
    [MemberData(nameof(WorkflowRunScripts))]
    public async Task Engines_project_workflow_ledger_runs(ScriptLanguage language, string source)
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var hostContext = ScriptHostContext.Empty.WithCapability<IScriptWorkflowLedger>(ledger);
        using var engine = CreateEngine(language, hostContext);

        var result = await ExecuteAsync(engine, language, source, hostContext);

        Assert.True(result.Success, result.Error);
        AssertSingleValue(result, "wf");
    }

    [Fact]
    public async Task Workflows_projection_uses_public_workspace_manager()
    {
        var root = Path.Combine(Path.GetTempPath(), "guida-scripting-workflows-" + Guid.NewGuid().ToString("N"));
        var workspace = new ScriptFileSystemWorkspace(root, new ScriptFileSystemWorkspaceOptions { CreateRoot = true });
        var manager = new ScriptWorkflowWorkspaceManager();
        await manager.CreateAsync(workspace, "wf");
        var hostContext = ScriptHostContext.Empty
            .WithCapability<IScriptWorkspace>(workspace)
            .WithCapability<IScriptWorkflowWorkspaceManager>(manager);
        using var engine = CreateEngine(ScriptLanguage.JavaScript, hostContext);

        try
        {
            var result = await ExecuteAsync(
                engine,
                ScriptLanguage.JavaScript,
                """
                async function main() {
                  await g.workflows.switch('wf');
                  const active = await g.workflows.getActive();
                  const workflows = await g.workflows.list();
                  return active.name + ':' + workflows.length;
                }
                """,
                hostContext);

            Assert.True(result.Success, result.Error);
            AssertSingleValue(result, "wf:1");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Worker_workflow_projection_uses_public_worker_context()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var upsert = await ledger.UpsertItemAsync(new ScriptWorkflowItemUpsert
        {
            WorkflowName = "wf",
            ItemKey = "item-1",
            Stage = "download",
            State = "pending"
        });
        var provider = new FixedWorkerWorkflowContextProvider(new ScriptWorkflowWorkerContext
        {
            WorkerName = "worker",
            JobId = "job-1",
            WorkflowName = "wf",
            ItemId = upsert.Value!.Id,
            ItemKey = "item-1",
            Stage = "download",
            State = "pending"
        });
        var hostContext = ScriptHostContext.Empty
            .WithCapability<IScriptWorkflowLedger>(ledger)
            .WithCapability<IScriptWorkflowWorkerContextProvider>(provider);
        using var engine = CreateEngine(ScriptLanguage.JavaScript, hostContext);

        var result = await ExecuteAsync(
            engine,
            ScriptLanguage.JavaScript,
            """
            async function main() {
              const ctx = g.worker.workflow.getContext();
              const item = await g.worker.workflow.getItem();
              return ctx.itemKey + ':' + item.itemKey;
            }
            """,
            hostContext);

        Assert.True(result.Success, result.Error);
        AssertSingleValue(result, "item-1:item-1");
    }

    public static TheoryData<ScriptLanguage, string> StoreScripts() =>
        new()
        {
            { ScriptLanguage.JavaScript, "async function main() { await g.store.put('docs', 'one', 'hello'); const doc = await g.store.get('docs', 'one'); return doc.data; }" },
            { ScriptLanguage.Lua, "g.store.put('docs', 'one', 'hello')\nlocal doc = g.store.get('docs', 'one')\nreturn doc.data" },
            { ScriptLanguage.Janet, "(do (g.store.put \"docs\" \"one\" \"hello\") (get (g.store.get \"docs\" \"one\") \"data\"))" }
        };

    public static TheoryData<ScriptLanguage, string> QueueScripts() =>
        new()
        {
            { ScriptLanguage.JavaScript, "async function main() { await g.queue.enqueue('jobs', 'payload', {}); const item = await g.queue.dequeue('jobs', {}); return item.data; }" },
            { ScriptLanguage.Lua, "g.queue.enqueue('jobs', 'payload')\nlocal item = g.queue.dequeue('jobs')\nreturn item.data" },
            { ScriptLanguage.Janet, "(do (g.queue.enqueue \"jobs\" \"payload\") (get (g.queue.dequeue \"jobs\") \"data\"))" }
        };

    public static TheoryData<ScriptLanguage, string> SearchScripts() =>
        new()
        {
            { ScriptLanguage.JavaScript, "async function main() { const results = await g.search.query('needle', {}); return results.items[0].title; }" },
            { ScriptLanguage.Lua, "local results = g.search.query('needle')\nreturn results.items[1].title" },
            { ScriptLanguage.Janet, "(do (def results (g.search.query \"needle\")) (get (get (get results \"items\") 0) \"title\"))" }
        };

    public static TheoryData<ScriptLanguage, string> WorkspaceScripts() =>
        new()
        {
            { ScriptLanguage.JavaScript, "async function main() { await g.workspace.writeFile('docs/one.txt', 'workspace', { createDirectories: true }); const file = await g.workspace.readFile('docs/one.txt'); return file.content; }" },
            { ScriptLanguage.Lua, "g.workspace.writeFile('docs/one.txt', 'workspace', { createDirectories = true })\nlocal file = g.workspace.readFile('docs/one.txt')\nreturn file.content" },
            { ScriptLanguage.Janet, "(do (g.workspace.writeFile \"docs/one.txt\" \"workspace\" @{\"createDirectories\" true}) (get (g.workspace.readFile \"docs/one.txt\") \"content\"))" }
        };

    public static TheoryData<ScriptLanguage, string> WorkflowRunScripts() =>
        new()
        {
            { ScriptLanguage.JavaScript, "async function main() { const run = await g.workflow.runs.start('wf', {}); return run.workflowName; }" },
            { ScriptLanguage.Lua, "local run = g.workflow.runs.start('wf')\nreturn run.workflowName" },
            { ScriptLanguage.Janet, "(get (g.workflow.runs.start \"wf\") \"workflowName\")" }
        };

    private static IScriptEngine CreateEngine(ScriptLanguage language, ScriptHostContext hostContext) =>
        new ScriptEngineFactory()
            .RegisterStandardEngines()
            .Create(new ScriptEngineCreationContext
            {
                Language = language,
                Name = $"projection.{ExtensionFor(language)}",
                HostContext = hostContext
            });

    private static Task<ScriptExecutionResult> ExecuteAsync(
        IScriptEngine engine,
        ScriptLanguage language,
        string source,
        ScriptHostContext hostContext) =>
        engine.ExecuteAsync(new ScriptExecutionRequest
        {
            Language = language,
            Source = source,
            Name = $"projection.{ExtensionFor(language)}",
            HostContext = hostContext
        });

    private static string ExtensionFor(ScriptLanguage language) =>
        language switch
        {
            ScriptLanguage.JavaScript => "js",
            ScriptLanguage.TypeScript => "ts",
            ScriptLanguage.Lua => "lua",
            ScriptLanguage.Janet => "janet",
            _ => "txt"
        };

    private static void AssertSingleValue(ScriptExecutionResult result, object expected)
    {
        var actual = Assert.Single(result.ReturnValues);
        if (actual is JsonElement json)
        {
            actual = json.ValueKind switch
            {
                JsonValueKind.Number => json.GetInt32(),
                JsonValueKind.String => json.GetString(),
                _ => json.ToString()
            };
        }

        if (expected is int expectedInteger && actual is double actualDouble)
        {
            Assert.Equal(expectedInteger, actualDouble);
            return;
        }

        Assert.Equal(expected, actual);
    }

    private sealed class CapturingHttpClient : IScriptHttpClient
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public Task<ScriptHttpResult<HttpResponseMessage>> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = CloneRequest(request);
            return Task.FromResult(ScriptHttpResult<HttpResponseMessage>.Succeeded(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok", Encoding.UTF8, "text/plain")
                }));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }

    private sealed class CapturingQueue : IScriptQueue
    {
        private readonly ScriptInMemoryQueue _inner = new();

        public ScriptQueueClaimOptions? LastClaimOptions { get; private set; }

        public Task<ScriptQueueResult<ScriptQueueItem>> EnqueueAsync(
            string queueName,
            ReadOnlyMemory<byte> payload,
            ScriptQueueEnqueueOptions? options = null,
            CancellationToken cancellationToken = default) =>
            _inner.EnqueueAsync(queueName, payload, options, cancellationToken);

        public Task<ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>> ClaimAsync(
            string queueName,
            ScriptQueueClaimOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastClaimOptions = options;
            return _inner.ClaimAsync(queueName, options, cancellationToken);
        }

        public Task<ScriptQueueResult> CompleteAsync(
            string queueName,
            string itemId,
            CancellationToken cancellationToken = default) =>
            _inner.CompleteAsync(queueName, itemId, cancellationToken);

        public Task<ScriptQueueResult> AbandonAsync(
            string queueName,
            string itemId,
            CancellationToken cancellationToken = default) =>
            _inner.AbandonAsync(queueName, itemId, cancellationToken);

        public Task<ScriptQueueResult<ScriptQueueItem>> GetAsync(
            string queueName,
            string itemId,
            CancellationToken cancellationToken = default) =>
            _inner.GetAsync(queueName, itemId, cancellationToken);

        public Task<ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>> ListAsync(
            string queueName,
            CancellationToken cancellationToken = default) =>
            _inner.ListAsync(queueName, cancellationToken);
    }

    private sealed class FixedWorkerWorkflowContextProvider : IScriptWorkflowWorkerContextProvider
    {
        public FixedWorkerWorkflowContextProvider(ScriptWorkflowWorkerContext current)
        {
            Current = current;
        }

        public ScriptWorkflowWorkerContext? Current { get; }
    }
}
