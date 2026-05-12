using System.Collections.Concurrent;
using JanetSharp;

namespace Guida.Scripting.Engines;

/// <summary>
/// Janet engine backed by JanetSharp.
/// </summary>
public sealed class JanetSharpScriptEngine : IScriptEngine
{
    private readonly ScriptEngineCreationContext _context;
    private readonly BlockingCollection<Action> _workQueue = new();
    private readonly Thread _thread;
    private JanetRuntime? _janet;
    private CancellationTokenSource? _runningTokenSource;
    private bool _disposed;

    public JanetSharpScriptEngine(ScriptEngineCreationContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _thread = new Thread(RunLoop)
        {
            Name = "JanetScriptEngine",
            IsBackground = true
        };
        _thread.Start();

        Invoke(() =>
        {
            _janet = new JanetRuntime();
            RegisterStandardGlobals();
        });
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
        if (!_disposed)
        {
            _runningTokenSource?.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _runningTokenSource?.Cancel();
        Invoke(() => _janet?.Dispose());
        _disposed = true;
        _workQueue.CompleteAdding();
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(1));
        }

        _workQueue.Dispose();
        _runningTokenSource?.Dispose();
    }

    private Task<ScriptExecutionResult> ExecuteCoreAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken)
    {
        _runningTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            foreach (var variable in request.Variables)
            {
                var name = variable.Key.Replace("\"", "\\\"", StringComparison.Ordinal);
                var value = ToJanetLiteral(variable.Value);
                Invoke(() => _janet!.Eval($"(def {name} {value})"));
            }

            var result = Invoke(() =>
            {
                try
                {
                    _runningTokenSource.Token.ThrowIfCancellationRequested();
                    return _janet!.Eval(request.Source);
                }
                catch (JanetException ex)
                {
                    var error = ex.ErrorValue.Type == JanetType.String
                        ? ex.ErrorValue.AsString()
                        : ex.Message;
                    throw new InvalidOperationException(error);
                }
            });

            _runningTokenSource.Token.ThrowIfCancellationRequested();
            return Task.FromResult(ToResult(result));
        }
        catch (Exception ex) when (IsCancellationException(ex))
        {
            throw new OperationCanceledException("Script execution canceled.", ex, cancellationToken);
        }
        catch (Exception ex)
        {
            ScriptEngineResultHelpers.Log(_context.HostContext, ScriptLogLevel.Error, ex.Message, "janet");
            return Task.FromResult(ScriptExecutionResult.Failed(ex.Message, ex));
        }
        finally
        {
            _runningTokenSource.Dispose();
            _runningTokenSource = null;
        }
    }

    private void RegisterStandardGlobals()
    {
        var projection = new ScriptApiProjection(_context.HostContext);
        _janet!.Eval("(def g @{})");

        _janet.Register("g.log", args =>
        {
            if (args.Length > 0)
            {
                ScriptEngineResultHelpers.Log(
                    _context.HostContext,
                    ScriptLogLevel.Information,
                    ConvertJanetValueToObject(args[0])?.ToString() ?? string.Empty,
                    "janet");
            }

            return Janet.Nil;
        });

        _janet.Register("g.wait", args =>
        {
            var milliseconds = args.Length > 0 ? (int)args[0].AsNumber() : 1000;
            if (milliseconds <= 0)
            {
                milliseconds = 1000;
            }

            Task.Delay(milliseconds, _runningTokenSource?.Token ?? CancellationToken.None).GetAwaiter().GetResult();
            return Janet.Nil;
        });

        RegisterApiFunction("g.store.put", args => projection.Store.Put(Arg<string>(args, 0), Arg<string>(args, 1), Arg(args, 2)));
        RegisterApiFunction("g.store.get", args => projection.Store.Get(Arg<string>(args, 0), Arg<string>(args, 1)));
        RegisterApiFunction("g.store.list", args => projection.Store.List(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.store.search", args => projection.Store.Search(Arg<string>(args, 0), Arg<string>(args, 1), Arg(args, 2)));
        RegisterApiFunction("g.store.delete", async args => await projection.Store.Delete(Arg<string>(args, 0), Arg<string>(args, 1)).ConfigureAwait(false));
        RegisterApiFunction("g.store.count", async args => await projection.Store.Count(Arg<string>(args, 0)).ConfigureAwait(false));
        RegisterApiFunction("g.store.clear", args => projection.Store.Clear(Arg<string>(args, 0)));
        RegisterApiFunction("g.store.collections", _ => projection.Store.Collections().ContinueWith<object?>(task => task.Result, TaskScheduler.Default));

        RegisterApiFunction("g.queue.enqueue", args => projection.Queue.Enqueue(Arg<string>(args, 0), Arg(args, 1), Arg(args, 2)));
        RegisterApiFunction("g.queue.dequeue", args => projection.Queue.Dequeue(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.queue.commit", args => projection.Queue.Commit(Arg<string>(args, 0)));
        RegisterApiFunction("g.queue.abort", async args => await projection.Queue.Abort(Arg<string>(args, 0), Arg(args, 1)?.ToString()).ConfigureAwait(false));
        RegisterApiFunction("g.queue.peek", args => projection.Queue.Peek(Arg<string>(args, 0)));
        RegisterApiFunction("g.queue.count", async args => await projection.Queue.Count(Arg<string>(args, 0)).ConfigureAwait(false));
        RegisterApiFunction("g.queue.clear", args => projection.Queue.Clear(Arg<string>(args, 0)));
        RegisterApiFunction("g.queue.list", args => projection.Queue.List(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.queue.queues", _ => Task.FromResult<object?>(projection.Queue.Queues()));
        RegisterApiFunction("g.queue.deadLetter", args => Task.FromResult(projection.Queue.DeadLetter(Arg<string>(args, 0), Arg(args, 1))));
        RegisterApiFunction("g.queue.retry", args => Task.FromResult(projection.Queue.Retry(Arg<string>(args, 0))));
        RegisterApiFunction("g.queue.waitForItem", args => projection.Queue.WaitForItem(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.queue.registerStrategy", args => Task.FromResult(projection.Queue.RegisterStrategy(Arg<string>(args, 0), Arg(args, 1))));

        RegisterApiFunction("g.http.request", args => projection.Http.Request(Arg<string>(args, 0), Arg<string>(args, 1), Arg(args, 2)));
        RegisterApiFunction("g.http.get", args => projection.Http.Get(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.http.post", args => projection.Http.Post(Arg<string>(args, 0), Arg(args, 1)));

        RegisterApiFunction("g.workspace.getEntry", args => projection.Workspace.GetEntry(Arg<string>(args, 0)));
        RegisterApiFunction("g.workspace.list", args => projection.Workspace.List(Arg<string>(args, 0)));
        RegisterApiFunction("g.workspace.readFile", args => projection.Workspace.ReadFile(Arg<string>(args, 0)));
        RegisterApiFunction("g.workspace.writeFile", args => projection.Workspace.WriteFile(Arg<string>(args, 0), Arg<string>(args, 1), Arg(args, 2)));

        RegisterApiFunction("g.search.query", args => projection.Search.Query(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.search.search", args => projection.Search.Search(Arg<string>(args, 0), Arg(args, 1)));

        RegisterApiFunction("g.workers.start", args => projection.Workers.Start(Arg(args, 0)));
        RegisterApiFunction("g.workers.stop", args => Task.FromResult(projection.Workers.Stop(Arg(args, 0)?.ToString())));
        RegisterApiFunction("g.workers.pause", _ => Task.FromResult(projection.Workers.Pause()));
        RegisterApiFunction("g.workers.resume", _ => Task.FromResult(projection.Workers.Resume()));
        RegisterApiFunction("g.workers.status", _ => projection.Workers.Status());

        RegisterApiFunction("g.worker.getContext", _ => Task.FromResult(projection.Worker.GetContext()));
        RegisterApiFunction("g.worker.workflow.getContext", _ => Task.FromResult(projection.Worker.Workflow.GetContext()));
        RegisterApiFunction("g.worker.workflow.getItem", _ => projection.Worker.Workflow.GetItem());
        RegisterApiFunction("g.worker.workflow.claim", args => projection.Worker.Workflow.Claim(Arg(args, 0)));
        RegisterApiFunction("g.worker.workflow.complete", args => projection.Worker.Workflow.Complete(Arg(args, 0)));
        RegisterApiFunction("g.worker.workflow.fail", args => projection.Worker.Workflow.Fail(Arg(args, 0)));
        RegisterApiFunction("g.worker.workflow.release", args => projection.Worker.Workflow.Release(Arg(args, 0)));
        RegisterApiFunction("g.worker.workflow.retry", args => projection.Worker.Workflow.Retry(Arg(args, 0)));
        RegisterApiFunction("g.worker.workflow.deadLetter", args => projection.Worker.Workflow.DeadLetter(Arg(args, 0)));

        RegisterApiFunction("g.workflow.runs.start", args => projection.Workflow.Runs.Start(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.runs.get", args => projection.Workflow.Runs.Get(Arg<string>(args, 0)));
        RegisterApiFunction("g.workflow.runs.list", args => projection.Workflow.Runs.List(Arg(args, 0)));
        RegisterApiFunction("g.workflow.runs.finish", args => projection.Workflow.Runs.Finish(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.runs.fail", args => projection.Workflow.Runs.Fail(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.items.upsert", args => projection.Workflow.Items.Upsert(Arg(args, 0)));
        RegisterApiFunction("g.workflow.items.get", args => projection.Workflow.Items.Get(Arg<string>(args, 0), Arg<string>(args, 1)));
        RegisterApiFunction("g.workflow.items.getById", args => projection.Workflow.Items.GetById(Arg<string>(args, 0)));
        RegisterApiFunction("g.workflow.items.query", args => projection.Workflow.Items.Query(Arg(args, 0)));
        RegisterApiFunction("g.workflow.items.setState", args => projection.Workflow.Items.SetState(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.items.appendEvent", args => projection.Workflow.Items.AppendEvent(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.items.getEvents", args => projection.Workflow.Items.GetEvents(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.items.attachArtifact", args => projection.Workflow.Items.AttachArtifact(Arg<string>(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.items.enqueue", args => projection.Workflow.Items.Enqueue(Arg(args, 0)));
        RegisterApiFunction("g.workflow.items.getArtifacts", args => projection.Workflow.Items.GetArtifacts(Arg<string>(args, 0)));
        RegisterApiFunction("g.workflow.items.claimNext", args => projection.Workflow.Items.ClaimNext(Arg(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.items.complete", args => projection.Workflow.Items.Complete(Arg(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.items.fail", args => projection.Workflow.Items.Fail(Arg(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.items.release", args => projection.Workflow.Items.Release(Arg(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.items.retry", args => projection.Workflow.Items.Retry(Arg(args, 0), Arg(args, 1)));
        RegisterApiFunction("g.workflow.items.deadLetter", args => projection.Workflow.Items.DeadLetter(Arg(args, 0), Arg(args, 1)));

        RegisterApiFunction("g.workflows.getActive", _ => projection.Workflows.GetActive());
        RegisterApiFunction("g.workflows.list", _ => projection.Workflows.List());
        RegisterApiFunction("g.workflows.switch", args => projection.Workflows.Switch(Arg<string>(args, 0)));
    }

    private void RegisterApiFunction(string name, Func<object?[], Task<object?>> callback)
    {
        _janet!.Register(name, args =>
        {
            var normalized = new object?[args.Length];
            for (var index = 0; index < args.Length; index++)
            {
                normalized[index] = ConvertJanetValueToObject(args[index]);
            }

            var result = callback(normalized).GetAwaiter().GetResult();
            return ConvertToJanetValue(result);
        });
    }

    private static object? Arg(object?[] args, int index) =>
        index < args.Length ? args[index] : null;

    private static T Arg<T>(object?[] args, int index)
    {
        if (index >= args.Length || args[index] is null)
        {
            throw new InvalidOperationException($"Argument {index + 1} is required.");
        }

        return (T)Convert.ChangeType(args[index], typeof(T), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private ScriptExecutionResult ToResult(Janet value)
    {
        if (value.Type == JanetType.Nil)
        {
            return ScriptExecutionResult.Succeeded();
        }

        if (value.Type == JanetType.Tuple)
        {
            using var tuple = value.AsTuple();
            return ScriptExecutionResult.Succeeded(tuple.Select(ConvertJanetValueToObject).Where(item => item is not null).ToArray());
        }

        return ScriptExecutionResult.Succeeded(ConvertJanetValueToObject(value));
    }

    private void RunLoop()
    {
        try
        {
            foreach (var action in _workQueue.GetConsumingEnumerable())
            {
                if (_disposed)
                {
                    break;
                }

                action();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Invoke(Action action)
    {
        if (_disposed)
        {
            return;
        }

        var tcs = new TaskCompletionSource();
        _workQueue.Add(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        tcs.Task.GetAwaiter().GetResult();
    }

    private T Invoke<T>(Func<T> action)
    {
        if (_disposed)
        {
            return default!;
        }

        var tcs = new TaskCompletionSource<T>();
        _workQueue.Add(() =>
        {
            try
            {
                tcs.SetResult(action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    private static Janet ConvertToJanetValue(object? value)
    {
        if (value is null)
        {
            return Janet.Nil;
        }

        if (value is Janet janet)
        {
            return janet;
        }

        if (value is bool boolean)
        {
            return Janet.From(boolean);
        }

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return Janet.From(Convert.ToDouble(value));
        }

        if (value is string text)
        {
            return Janet.From(text);
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            var table = JanetTable.Create();
            foreach (var key in dictionary.Keys)
            {
                table[ConvertToJanetValue(key)] = ConvertToJanetValue(dictionary[key]);
            }

            return table.Value;
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            var array = JanetArray.Create();
            foreach (var item in enumerable)
            {
                array.Add(ConvertToJanetValue(item));
            }

            return array.Value;
        }

        return Janet.From(value.ToString() ?? string.Empty);
    }

    private static object? ConvertJanetValueToObject(Janet value)
    {
        return value.Type switch
        {
            JanetType.Nil => null,
            JanetType.Boolean => value.AsBoolean(),
            JanetType.Number => value.AsNumber(),
            JanetType.String => value.AsString(),
            JanetType.Symbol => value.AsString(),
            JanetType.Keyword => value.AsString(),
            JanetType.Array => ConvertJanetArray(value),
            JanetType.Tuple => ConvertJanetTuple(value),
            JanetType.Table => ConvertJanetTable(value),
            JanetType.Struct => ConvertJanetStruct(value),
            _ => value.ToString()
        };
    }

    private static IReadOnlyList<object?> ConvertJanetArray(Janet value)
    {
        using var array = value.AsArray();
        return array.Select(ConvertJanetValueToObject).ToList();
    }

    private static IReadOnlyList<object?> ConvertJanetTuple(Janet value)
    {
        using var tuple = value.AsTuple();
        return tuple.Select(ConvertJanetValueToObject).ToList();
    }

    private static IReadOnlyDictionary<string, object?> ConvertJanetTable(Janet value)
    {
        using var table = value.AsTable();
        return table.ToDictionary(pair => ToKey(pair.Key), pair => ConvertJanetValueToObject(pair.Value), StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?> ConvertJanetStruct(Janet value)
    {
        using var @struct = value.AsStruct();
        return @struct.ToDictionary(pair => ToKey(pair.Key), pair => ConvertJanetValueToObject(pair.Value), StringComparer.Ordinal);
    }

    private static string ToKey(Janet value) =>
        value.Type is JanetType.String or JanetType.Keyword or JanetType.Symbol
            ? value.AsString() ?? string.Empty
            : value.ToString();

    private static string ToJanetLiteral(object? value)
    {
        return value switch
        {
            null => "nil",
            bool boolean => boolean ? "true" : "false",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "nil",
            string text => $"\"{EscapeJanetString(text)}\"",
            _ => $"\"{EscapeJanetString(value.ToString() ?? string.Empty)}\""
        };
    }

    private static string EscapeJanetString(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private static bool IsCancellationException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
            {
                return true;
            }

            if (string.Equals(current.Message, "A task was canceled.", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
