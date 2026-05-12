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

        return value.ToString();
    }

    private void RegisterStandardGlobals()
    {
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
                    "javascript"))
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
