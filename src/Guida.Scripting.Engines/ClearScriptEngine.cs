using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;

namespace Guida.Scripting.Engines;

/// <summary>
/// JavaScript engine backed by ClearScript V8.
/// </summary>
public sealed class ClearScriptEngine : IScriptEngine
{
    private static readonly Regex ModuleSyntax = new(
        @"^\s*(import|export)\s+",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly ScriptEngineCreationContext _context;
    private readonly V8ScriptEngine _engine;
    private CancellationTokenSource? _runningTokenSource;
    private bool _disposed;

    public ClearScriptEngine(ScriptEngineCreationContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        _engine = new V8ScriptEngine(
            V8ScriptEngineFlags.EnableTaskPromiseConversion |
            V8ScriptEngineFlags.EnableValueTaskPromiseConversion |
            V8ScriptEngineFlags.EnableDynamicModuleImports |
            V8ScriptEngineFlags.EnableStringifyEnhancements);

        if (_context.HostContext.TryGetCapability<IScriptDocumentProvider>(out var documentProvider) &&
            documentProvider is not null)
        {
            _engine.DocumentSettings.Loader = new ScriptDocumentProviderLoader(documentProvider);
            _engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
        }

        RegisterStandardGlobals();
    }

    public Task<ScriptExecutionResult> ExecuteAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        return ScriptEngineResultHelpers.RunWithTiming(
            token => ExecuteCoreAsync(request, token),
            request.Timeout,
            cancellationToken);
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _runningTokenSource?.Cancel();

        try
        {
            _engine.CancelAwaitDebugger();
            _engine.Interrupt();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runningTokenSource?.Cancel();
        _runningTokenSource?.Dispose();
        _engine.Dispose();
    }

    private async Task<ScriptExecutionResult> ExecuteCoreAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken)
    {
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runningTokenSource = runCts;

        foreach (var variable in request.Variables)
        {
            _engine.Script[variable.Key] = variable.Value;
        }

        try
        {
            var isModule = ModuleSyntax.IsMatch(request.Source);
            var result = isModule
                ? _engine.Evaluate(CreateDocumentInfo(request, isModule: true), ExposeModuleMain(request.Source))
                : ExecuteScriptOrMain(request);

            result = await UnwrapTaskAsync(result, runCts.Token).ConfigureAwait(false);

            if (isModule && Regex.IsMatch(request.Source, @"\bfunction\s+main\b"))
            {
                result = _engine.Evaluate("globalThis.__guida_module_main ? globalThis.__guida_module_main() : undefined");
                _engine.Execute("delete globalThis.__guida_module_main;");
                result = await UnwrapTaskAsync(result, runCts.Token).ConfigureAwait(false);
            }

            return ToResult(result);
        }
        catch (ScriptInterruptedException)
        {
            return ScriptExecutionResult.Canceled("Script execution interrupted.");
        }
        catch (ScriptEngineException ex)
        {
            var error = FormatScriptError(ex);
            ScriptEngineResultHelpers.Log(_context.HostContext, ScriptLogLevel.Error, error, "javascript");
            return ScriptExecutionResult.Failed(error, ex);
        }
        finally
        {
            _runningTokenSource = null;
        }
    }

    private object? ExecuteScriptOrMain(ScriptExecutionRequest request)
    {
        if (Regex.IsMatch(request.Source, @"async\s+function\s+main\s*\(") ||
            Regex.IsMatch(request.Source, @"function\s+main\s*\("))
        {
            _engine.Execute(CreateDocumentInfo(request, isModule: false), request.Source);
            return _engine.Evaluate("main()");
        }

        return _engine.Evaluate(CreateDocumentInfo(request, isModule: false), request.Source);
    }

    private static string ExposeModuleMain(string source) =>
        Regex.IsMatch(source, @"\bfunction\s+main\b")
            ? source + "\n\nif (typeof main === 'function') { globalThis.__guida_module_main = main; }"
            : source;

    private static DocumentInfo CreateDocumentInfo(ScriptExecutionRequest request, bool isModule)
    {
        if (Uri.TryCreate(request.Name, UriKind.Absolute, out var uri))
        {
            var info = new DocumentInfo(uri);
            if (isModule)
            {
                info.Category = ModuleCategory.Standard;
            }

            return info;
        }

        var documentInfo = new DocumentInfo(string.IsNullOrWhiteSpace(request.Name) ? "script.js" : request.Name);
        if (isModule)
        {
            documentInfo.Category = ModuleCategory.Standard;
        }

        return documentInfo;
    }

    private async Task<object?> UnwrapTaskAsync(object? result, CancellationToken cancellationToken)
    {
        if (result is Task<object?> objectTask)
        {
            return await objectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (result is Task<object> nonNullableObjectTask)
        {
            return await nonNullableObjectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (result is Task task)
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return Undefined.Value;
        }

        return result;
    }

    private ScriptExecutionResult ToResult(object? value)
    {
        if (value is null or Undefined or VoidResult)
        {
            return ScriptExecutionResult.Succeeded();
        }

        return ScriptExecutionResult.Succeeded(SnapshotReturnValue(value));
    }

    private object? SnapshotReturnValue(object value)
    {
        if (value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return value;
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.Clone();
        }

        if (value is ScriptObject)
        {
            try
            {
                dynamic stringify = _engine.Evaluate("(value => JSON.stringify(value))");
                object? jsonValue = stringify(value);
                var json = Convert.ToString(jsonValue);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    return JsonSerializer.Deserialize<JsonElement>(json).Clone();
                }
            }
            catch
            {
            }
        }

        if (value is System.Collections.IDictionary or System.Collections.IEnumerable)
        {
            try
            {
                var json = JsonSerializer.Serialize(value);
                return JsonSerializer.Deserialize<JsonElement>(json).Clone();
            }
            catch
            {
            }
        }

        return value.ToString();
    }

    private void RegisterStandardGlobals()
    {
        var projection = new ScriptApiProjection(_context.HostContext);
        var g = new PropertyBag
        {
            ["wait"] = new Func<int, Task<bool>>(async milliseconds =>
            {
                if (milliseconds <= 0)
                {
                    milliseconds = 1000;
                }

                await Task.Delay(milliseconds, _runningTokenSource?.Token ?? CancellationToken.None).ConfigureAwait(false);
                return true;
            }),
            ["log"] = new Action<object?>(message =>
                ScriptEngineResultHelpers.Log(
                    _context.HostContext,
                    ScriptLogLevel.Information,
                    FormatLogMessage(message),
                    "javascript")),
            ["store"] = new PropertyBag
            {
                ["put"] = new Func<string, string, object?, Task<object?>>((collection, key, data) => ToJavaScriptValueAsync(projection.Store.Put(collection, key, NormalizeHostValue(data)))),
                ["get"] = new Func<string, string, Task<object?>>((collection, key) => ToJavaScriptValueAsync(projection.Store.Get(collection, key))),
                ["list"] = new Func<string, object?, Task<object?>>((collection, options) => ToJavaScriptValueAsync(projection.Store.List(collection, NormalizeHostValue(options)))),
                ["search"] = new Func<string, string, object?, Task<object?>>((collection, query, options) => ToJavaScriptValueAsync(projection.Store.Search(collection, query, NormalizeHostValue(options)))),
                ["delete"] = new Func<string, string, Task<bool>>((collection, key) => projection.Store.Delete(collection, key)),
                ["count"] = new Func<string, Task<int>>(collection => projection.Store.Count(collection)),
                ["clear"] = new Func<string, Task<object?>>(collection => ToJavaScriptValueAsync(projection.Store.Clear(collection))),
                ["collections"] = new Func<Task<string[]>>(() => projection.Store.Collections())
            },
            ["queue"] = new PropertyBag
            {
                ["enqueue"] = new Func<string, object?, object?, Task<object?>>((name, data, options) => ToJavaScriptValueAsync(projection.Queue.Enqueue(name, NormalizeHostValue(data), NormalizeHostValue(options)))),
                ["dequeue"] = new Func<string, object?, Task<object?>>((name, options) => ToJavaScriptValueAsync(projection.Queue.Dequeue(name, NormalizeHostValue(options)))),
                ["commit"] = new Func<string, Task<object?>>(checkoutId => ToJavaScriptValueAsync(projection.Queue.Commit(checkoutId))),
                ["abort"] = new Func<string, string?, Task<bool>>((checkoutId, error) => projection.Queue.Abort(checkoutId, error)),
                ["peek"] = new Func<string, Task<object?>>(name => ToJavaScriptValueAsync(projection.Queue.Peek(name))),
                ["count"] = new Func<string, Task<int>>(name => projection.Queue.Count(name)),
                ["clear"] = new Func<string, Task<object?>>(name => ToJavaScriptValueAsync(projection.Queue.Clear(name))),
                ["list"] = new Func<string, object?, Task<object?>>((name, options) => ToJavaScriptValueAsync(projection.Queue.List(name, NormalizeHostValue(options)))),
                ["queues"] = new Func<string[]>(() => projection.Queue.Queues()),
                ["deadLetter"] = new Func<string, object?, object?>((name, options) => ToJavaScriptValue(projection.Queue.DeadLetter(name, NormalizeHostValue(options)))),
                ["retry"] = new Func<string, object?>(itemId => ToJavaScriptValue(projection.Queue.Retry(itemId))),
                ["waitForItem"] = new Func<string, object?, Task<object?>>((name, options) => ToJavaScriptValueAsync(projection.Queue.WaitForItem(name, NormalizeHostValue(options)))),
                ["registerStrategy"] = new Func<string, object?, object?>((name, fnOrPath) => ToJavaScriptValue(projection.Queue.RegisterStrategy(name, NormalizeHostValue(fnOrPath))))
            },
            ["http"] = new PropertyBag
            {
                ["request"] = new Func<string, string, object?, Task<object?>>((method, url, options) => ToJavaScriptValueAsync(projection.Http.Request(method, url, NormalizeHostValue(options)))),
                ["get"] = new Func<string, object?, Task<object?>>((url, options) => ToJavaScriptValueAsync(projection.Http.Get(url, NormalizeHostValue(options)))),
                ["post"] = new Func<string, object?, Task<object?>>((url, options) => ToJavaScriptValueAsync(projection.Http.Post(url, NormalizeHostValue(options))))
            },
            ["workspace"] = new PropertyBag
            {
                ["getEntry"] = new Func<string, Task<object?>>(path => ToJavaScriptValueAsync(projection.Workspace.GetEntry(path))),
                ["list"] = new Func<string, Task<object?>>(path => ToJavaScriptValueAsync(projection.Workspace.List(path))),
                ["readFile"] = new Func<string, Task<object?>>(path => ToJavaScriptValueAsync(projection.Workspace.ReadFile(path))),
                ["writeFile"] = new Func<string, string, object?, Task<object?>>((path, content, options) => ToJavaScriptValueAsync(projection.Workspace.WriteFile(path, content, NormalizeHostValue(options))))
            },
            ["search"] = new PropertyBag
            {
                ["query"] = new Func<string, object?, Task<object?>>((query, options) => ToJavaScriptValueAsync(projection.Search.Query(query, NormalizeHostValue(options)))),
                ["search"] = new Func<string, object?, Task<object?>>((query, options) => ToJavaScriptValueAsync(projection.Search.Search(query, NormalizeHostValue(options))))
            },
            ["workers"] = new PropertyBag
            {
                ["start"] = new Func<object?, Task<object?>>(options => ToJavaScriptValueAsync(projection.Workers.Start(NormalizeHostValue(options)))),
                ["stop"] = new Func<string?, object?>(queueName => projection.Workers.Stop(queueName)),
                ["pause"] = new Func<object?>(() => projection.Workers.Pause()),
                ["resume"] = new Func<object?>(() => projection.Workers.Resume()),
                ["status"] = new Func<Task<object?>>(() => ToJavaScriptValueAsync(projection.Workers.Status()))
            },
            ["worker"] = new PropertyBag
            {
                ["getContext"] = new Func<object?>(() => ToJavaScriptValue(projection.Worker.GetContext())),
                ["workflow"] = new PropertyBag
                {
                    ["getContext"] = new Func<object?>(() => ToJavaScriptValue(projection.Worker.Workflow.GetContext())),
                    ["getItem"] = new Func<Task<object?>>(() => ToJavaScriptValueAsync(projection.Worker.Workflow.GetItem())),
                    ["claim"] = new Func<object?, Task<object?>>(options => ToJavaScriptValueAsync(projection.Worker.Workflow.Claim(NormalizeHostValue(options)))),
                    ["complete"] = new Func<object?, Task<object?>>(options => ToJavaScriptValueAsync(projection.Worker.Workflow.Complete(NormalizeHostValue(options)))),
                    ["fail"] = new Func<object?, Task<object?>>(errorOrOptions => ToJavaScriptValueAsync(projection.Worker.Workflow.Fail(NormalizeHostValue(errorOrOptions)))),
                    ["release"] = new Func<object?, Task<object?>>(options => ToJavaScriptValueAsync(projection.Worker.Workflow.Release(NormalizeHostValue(options)))),
                    ["retry"] = new Func<object?, Task<object?>>(options => ToJavaScriptValueAsync(projection.Worker.Workflow.Retry(NormalizeHostValue(options)))),
                    ["deadLetter"] = new Func<object?, Task<object?>>(reasonOrOptions => ToJavaScriptValueAsync(projection.Worker.Workflow.DeadLetter(NormalizeHostValue(reasonOrOptions))))
                }
            },
            ["workflow"] = new PropertyBag
            {
                ["runs"] = new PropertyBag
                {
                    ["start"] = new Func<string, object?, Task<object?>>((workflowName, options) => ToJavaScriptValueAsync(projection.Workflow.Runs.Start(workflowName, NormalizeHostValue(options)))),
                    ["get"] = new Func<string, Task<object?>>(runId => ToJavaScriptValueAsync(projection.Workflow.Runs.Get(runId))),
                    ["list"] = new Func<object?, Task<object?>>(filter => ToJavaScriptValueAsync(projection.Workflow.Runs.List(NormalizeHostValue(filter)))),
                    ["finish"] = new Func<string, object?, Task<object?>>((runId, options) => ToJavaScriptValueAsync(projection.Workflow.Runs.Finish(runId, NormalizeHostValue(options)))),
                    ["fail"] = new Func<string, object?, Task<object?>>((runId, errorOrOptions) => ToJavaScriptValueAsync(projection.Workflow.Runs.Fail(runId, NormalizeHostValue(errorOrOptions))))
                },
                ["items"] = new PropertyBag
                {
                    ["upsert"] = new Func<object?, Task<object?>>(input => ToJavaScriptValueAsync(projection.Workflow.Items.Upsert(NormalizeHostValue(input)))),
                    ["get"] = new Func<string, string, Task<object?>>((workflowName, itemKey) => ToJavaScriptValueAsync(projection.Workflow.Items.Get(workflowName, itemKey))),
                    ["getById"] = new Func<string, Task<object?>>(itemId => ToJavaScriptValueAsync(projection.Workflow.Items.GetById(itemId))),
                    ["query"] = new Func<object?, Task<object?>>(filter => ToJavaScriptValueAsync(projection.Workflow.Items.Query(NormalizeHostValue(filter)))),
                    ["setState"] = new Func<string, object?, Task<object?>>((itemId, update) => ToJavaScriptValueAsync(projection.Workflow.Items.SetState(itemId, NormalizeHostValue(update)))),
                    ["appendEvent"] = new Func<string, object?, Task<object?>>((itemId, input) => ToJavaScriptValueAsync(projection.Workflow.Items.AppendEvent(itemId, NormalizeHostValue(input)))),
                    ["getEvents"] = new Func<string, object?, Task<object?>>((itemId, options) => ToJavaScriptValueAsync(projection.Workflow.Items.GetEvents(itemId, NormalizeHostValue(options)))),
                    ["attachArtifact"] = new Func<string, object?, Task<object?>>((itemId, input) => ToJavaScriptValueAsync(projection.Workflow.Items.AttachArtifact(itemId, NormalizeHostValue(input)))),
                    ["getArtifacts"] = new Func<string, Task<object?>>(itemId => ToJavaScriptValueAsync(projection.Workflow.Items.GetArtifacts(itemId))),
                    ["claimNext"] = new Func<object?, object?, Task<object?>>((filter, leaseOptions) => ToJavaScriptValueAsync(projection.Workflow.Items.ClaimNext(NormalizeHostValue(filter), NormalizeHostValue(leaseOptions)))),
                    ["complete"] = new Func<object?, object?, Task<object?>>((itemRef, options) => ToJavaScriptValueAsync(projection.Workflow.Items.Complete(NormalizeHostValue(itemRef), NormalizeHostValue(options)))),
                    ["fail"] = new Func<object?, object?, Task<object?>>((itemRef, errorOrOptions) => ToJavaScriptValueAsync(projection.Workflow.Items.Fail(NormalizeHostValue(itemRef), NormalizeHostValue(errorOrOptions)))),
                    ["release"] = new Func<object?, object?, Task<object?>>((itemRef, options) => ToJavaScriptValueAsync(projection.Workflow.Items.Release(NormalizeHostValue(itemRef), NormalizeHostValue(options)))),
                    ["retry"] = new Func<object?, object?, Task<object?>>((itemRef, options) => ToJavaScriptValueAsync(projection.Workflow.Items.Retry(NormalizeHostValue(itemRef), NormalizeHostValue(options)))),
                    ["deadLetter"] = new Func<object?, object?, Task<object?>>((itemRef, reasonOrOptions) => ToJavaScriptValueAsync(projection.Workflow.Items.DeadLetter(NormalizeHostValue(itemRef), NormalizeHostValue(reasonOrOptions)))),
                    ["enqueue"] = new Func<object?, Task<object?>>(input => ToJavaScriptValueAsync(projection.Workflow.Items.Enqueue(NormalizeHostValue(input))))
                }
            },
            ["workflows"] = new PropertyBag
            {
                ["getActive"] = new Func<Task<object?>>(() => ToJavaScriptValueAsync(projection.Workflows.GetActive())),
                ["list"] = new Func<Task<object?>>(() => ToJavaScriptValueAsync(projection.Workflows.List())),
                ["switch"] = new Func<string, Task<object?>>(name => ToJavaScriptValueAsync(projection.Workflows.Switch(name)))
            }
        };

        _engine.AddHostObject("g", g);

        var console = new PropertyBag
        {
            ["log"] = new Action<object?>(message => ScriptEngineResultHelpers.Log(_context.HostContext, ScriptLogLevel.Information, FormatLogMessage(message), "javascript")),
            ["warn"] = new Action<object?>(message => ScriptEngineResultHelpers.Log(_context.HostContext, ScriptLogLevel.Warning, FormatLogMessage(message), "javascript")),
            ["error"] = new Action<object?>(message => ScriptEngineResultHelpers.Log(_context.HostContext, ScriptLogLevel.Error, FormatLogMessage(message), "javascript"))
        };

        _engine.AddHostObject("console", console);
        _engine.AddHostObject("btoa", new Func<string, string>(value => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))));
        _engine.AddHostObject("atob", new Func<string, string>(value => Encoding.UTF8.GetString(Convert.FromBase64String(value))));

        var crypto = new PropertyBag
        {
            ["randomUUID"] = new Func<string>(() => Guid.NewGuid().ToString())
        };
        _engine.AddHostObject("crypto", crypto);
    }

    private object? NormalizeHostValue(object? value)
    {
        if (value is null or Undefined or VoidResult)
        {
            return null;
        }

        if (value is ScriptObject)
        {
            try
            {
                dynamic stringify = _engine.Evaluate("(value => JSON.stringify(value))");
                object? jsonValue = stringify(value);
                var json = Convert.ToString(jsonValue);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var document = JsonDocument.Parse(json);
                    return NormalizeJsonElement(document.RootElement);
                }
            }
            catch
            {
            }
        }

        return value;
    }

    private async Task<object?> ToJavaScriptValueAsync(Task<object?> task) =>
        ToJavaScriptValue(await task.ConfigureAwait(false));

    private object? ToJavaScriptValue(object? value)
    {
        if (value is null ||
            value is string ||
            value is bool ||
            value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return value;
        }

        try
        {
            var json = JsonSerializer.Serialize(value);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            dynamic parse = _engine.Evaluate("(json => JSON.parse(json))");
            return parse(json);
        }
        catch
        {
            return value;
        }
    }

    private static object? NormalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => NormalizeJsonElement(property.Value),
                StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private string FormatLogMessage(object? message)
    {
        if (message is null)
        {
            return "null";
        }

        if (message is Undefined)
        {
            return "undefined";
        }

        if (message is string text)
        {
            return text;
        }

        if (message is ScriptObject)
        {
            try
            {
                dynamic stringify = _engine.Evaluate("(value => JSON.stringify(value, null, 2))");
                object? json = stringify(message);
                if (json is not null and not Undefined)
                {
                    return Convert.ToString(json) ?? string.Empty;
                }
            }
            catch
            {
            }
        }

        return message.ToString() ?? string.Empty;
    }

    private static string FormatScriptError(ScriptEngineException ex)
    {
        var details = ex.ErrorDetails ?? string.Empty;
        var lineMatch = Regex.Match(details, @"^[^:]*:(\d+):");
        return lineMatch.Success
            ? $"Line {lineMatch.Groups[1].Value}: {ex.Message}"
            : ex.Message;
    }

    private sealed class ScriptDocumentProviderLoader : DocumentLoader
    {
        private readonly IScriptDocumentProvider _documentProvider;

        public ScriptDocumentProviderLoader(IScriptDocumentProvider documentProvider)
        {
            _documentProvider = documentProvider;
        }

        public override async Task<Document> LoadDocumentAsync(
            DocumentSettings settings,
            DocumentInfo? sourceInfo,
            string specifier,
            DocumentCategory category,
            DocumentContextCallback contextCallback)
        {
            var loaded = await _documentProvider.LoadAsync(
                specifier,
                new ScriptDocumentLoadOptions { Language = ScriptLanguage.JavaScript }).ConfigureAwait(false);

            if (!loaded.Success)
            {
                throw new FileNotFoundException(loaded.Error?.Message ?? $"Module '{specifier}' could not be loaded.", specifier);
            }

            var document = loaded.Value!;
            var info = document.SourceUri is not null
                ? new DocumentInfo(document.SourceUri) { Category = category }
                : new DocumentInfo(string.IsNullOrWhiteSpace(document.Name) ? specifier : document.Name) { Category = category };

            return new StringDocument(info, document.Source);
        }
    }
}
