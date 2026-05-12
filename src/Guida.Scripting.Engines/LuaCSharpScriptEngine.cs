using Lua;
using Lua.Standard;

namespace Guida.Scripting.Engines;

/// <summary>
/// Lua engine backed by LuaCSharp.
/// </summary>
public sealed class LuaCSharpScriptEngine : IScriptEngine
{
    private readonly ScriptEngineCreationContext _context;
    private readonly LuaState _lua;
    private readonly Dictionary<string, object?> _variables = new(StringComparer.Ordinal);
    private CancellationTokenSource? _runningTokenSource;
    private bool _disposed;

    public LuaCSharpScriptEngine(ScriptEngineCreationContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _lua = LuaState.Create();
        _lua.OpenStandardLibraries();
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

        _disposed = true;
        _runningTokenSource?.Cancel();
        _runningTokenSource?.Dispose();
        _lua.Dispose();
    }

    private async Task<ScriptExecutionResult> ExecuteCoreAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken)
    {
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runningTokenSource = runCts;

        foreach (var variable in request.Variables)
        {
            _variables[variable.Key] = variable.Value;
            _lua.Environment[variable.Key] = ConvertToLuaValue(variable.Value);
        }

        try
        {
            var values = await _lua.DoStringAsync(request.Source).AsTask().ConfigureAwait(false);
            runCts.Token.ThrowIfCancellationRequested();
            if (values is null)
            {
                return ScriptExecutionResult.Succeeded();
            }

            return ScriptExecutionResult.Succeeded(values.Select(ConvertLuaValueToObject).ToArray());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ScriptEngineResultHelpers.Log(_context.HostContext, ScriptLogLevel.Error, ex.Message, "lua");
            return ScriptExecutionResult.Failed(ex.Message, ex);
        }
        finally
        {
            _runningTokenSource = null;
        }
    }

    private void RegisterStandardGlobals()
    {
        var projection = new ScriptApiProjection(_context.HostContext);
        var g = new LuaTable();

        g["wait"] = new LuaFunction(async (context, cancellationToken) =>
        {
            var milliseconds = context.HasArgument(0) ? (int)context.GetArgument<double>(0) : 1000;
            if (milliseconds <= 0)
            {
                milliseconds = 1000;
            }

            var token = _runningTokenSource?.Token ?? cancellationToken;
            await Task.Delay(milliseconds, token).ConfigureAwait(false);
            return 0;
        });

        g["log"] = new LuaFunction((context, _) =>
        {
            var message = context.HasArgument(0)
                ? context.GetArgument<string>(0)
                : string.Empty;
            ScriptEngineResultHelpers.Log(_context.HostContext, ScriptLogLevel.Information, message, "lua");
            return new ValueTask<int>(0);
        });

        g["store"] = new LuaTable
        {
            ["put"] = LuaApiFunction(args => projection.Store.Put(Arg<string>(args, 0), Arg<string>(args, 1), args.ElementAtOrDefault(2))),
            ["get"] = LuaApiFunction(args => projection.Store.Get(Arg<string>(args, 0), Arg<string>(args, 1))),
            ["list"] = LuaApiFunction(args => projection.Store.List(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
            ["search"] = LuaApiFunction(args => projection.Store.Search(Arg<string>(args, 0), Arg<string>(args, 1), args.ElementAtOrDefault(2))),
            ["delete"] = LuaApiFunction(async args => await projection.Store.Delete(Arg<string>(args, 0), Arg<string>(args, 1)).ConfigureAwait(false)),
            ["count"] = LuaApiFunction(async args => await projection.Store.Count(Arg<string>(args, 0)).ConfigureAwait(false)),
            ["clear"] = LuaApiFunction(args => projection.Store.Clear(Arg<string>(args, 0))),
            ["collections"] = LuaApiFunction(_ => projection.Store.Collections().ContinueWith<object?>(task => task.Result, TaskScheduler.Default))
        };

        g["queue"] = new LuaTable
        {
            ["enqueue"] = LuaApiFunction(args => projection.Queue.Enqueue(Arg<string>(args, 0), args.ElementAtOrDefault(1), args.ElementAtOrDefault(2))),
            ["dequeue"] = LuaApiFunction(args => projection.Queue.Dequeue(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
            ["commit"] = LuaApiFunction(args => projection.Queue.Commit(Arg<string>(args, 0))),
            ["abort"] = LuaApiFunction(async args => await projection.Queue.Abort(Arg<string>(args, 0), args.ElementAtOrDefault(1)?.ToString()).ConfigureAwait(false)),
            ["peek"] = LuaApiFunction(args => projection.Queue.Peek(Arg<string>(args, 0))),
            ["count"] = LuaApiFunction(async args => await projection.Queue.Count(Arg<string>(args, 0)).ConfigureAwait(false)),
            ["clear"] = LuaApiFunction(args => projection.Queue.Clear(Arg<string>(args, 0))),
            ["list"] = LuaApiFunction(args => projection.Queue.List(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
            ["queues"] = LuaApiFunction(args => Task.FromResult<object?>(projection.Queue.Queues())),
            ["deadLetter"] = LuaApiFunction(args => Task.FromResult(projection.Queue.DeadLetter(Arg<string>(args, 0), args.ElementAtOrDefault(1)))),
            ["retry"] = LuaApiFunction(args => Task.FromResult(projection.Queue.Retry(Arg<string>(args, 0)))),
            ["waitForItem"] = LuaApiFunction(args => projection.Queue.WaitForItem(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
            ["registerStrategy"] = LuaApiFunction(args => Task.FromResult(projection.Queue.RegisterStrategy(Arg<string>(args, 0), args.ElementAtOrDefault(1))))
        };

        g["http"] = new LuaTable
        {
            ["request"] = LuaApiFunction(args => projection.Http.Request(Arg<string>(args, 0), Arg<string>(args, 1), args.ElementAtOrDefault(2))),
            ["get"] = LuaApiFunction(args => projection.Http.Get(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
            ["post"] = LuaApiFunction(args => projection.Http.Post(Arg<string>(args, 0), args.ElementAtOrDefault(1)))
        };

        g["workspace"] = new LuaTable
        {
            ["getEntry"] = LuaApiFunction(args => projection.Workspace.GetEntry(Arg<string>(args, 0))),
            ["list"] = LuaApiFunction(args => projection.Workspace.List(Arg<string>(args, 0))),
            ["readFile"] = LuaApiFunction(args => projection.Workspace.ReadFile(Arg<string>(args, 0))),
            ["writeFile"] = LuaApiFunction(args => projection.Workspace.WriteFile(Arg<string>(args, 0), Arg<string>(args, 1), args.ElementAtOrDefault(2)))
        };

        g["search"] = new LuaTable
        {
            ["query"] = LuaApiFunction(args => projection.Search.Query(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
            ["search"] = LuaApiFunction(args => projection.Search.Search(Arg<string>(args, 0), args.ElementAtOrDefault(1)))
        };

        g["workers"] = new LuaTable
        {
            ["start"] = LuaApiFunction(args => projection.Workers.Start(args.ElementAtOrDefault(0))),
            ["stop"] = LuaApiFunction(args => Task.FromResult(projection.Workers.Stop(args.ElementAtOrDefault(0)?.ToString()))),
            ["pause"] = LuaApiFunction(args => Task.FromResult(projection.Workers.Pause())),
            ["resume"] = LuaApiFunction(args => Task.FromResult(projection.Workers.Resume())),
            ["status"] = LuaApiFunction(args => projection.Workers.Status())
        };

        g["worker"] = new LuaTable
        {
            ["getContext"] = LuaApiFunction(args => Task.FromResult(projection.Worker.GetContext())),
            ["workflow"] = new LuaTable
            {
                ["getContext"] = LuaApiFunction(args => Task.FromResult(projection.Worker.Workflow.GetContext())),
                ["getItem"] = LuaApiFunction(args => projection.Worker.Workflow.GetItem()),
                ["claim"] = LuaApiFunction(args => projection.Worker.Workflow.Claim(args.ElementAtOrDefault(0))),
                ["complete"] = LuaApiFunction(args => projection.Worker.Workflow.Complete(args.ElementAtOrDefault(0))),
                ["fail"] = LuaApiFunction(args => projection.Worker.Workflow.Fail(args.ElementAtOrDefault(0))),
                ["release"] = LuaApiFunction(args => projection.Worker.Workflow.Release(args.ElementAtOrDefault(0))),
                ["retry"] = LuaApiFunction(args => projection.Worker.Workflow.Retry(args.ElementAtOrDefault(0))),
                ["deadLetter"] = LuaApiFunction(args => projection.Worker.Workflow.DeadLetter(args.ElementAtOrDefault(0)))
            }
        };

        g["workflow"] = new LuaTable
        {
            ["runs"] = new LuaTable
            {
                ["start"] = LuaApiFunction(args => projection.Workflow.Runs.Start(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
                ["get"] = LuaApiFunction(args => projection.Workflow.Runs.Get(Arg<string>(args, 0))),
                ["list"] = LuaApiFunction(args => projection.Workflow.Runs.List(args.ElementAtOrDefault(0))),
                ["finish"] = LuaApiFunction(args => projection.Workflow.Runs.Finish(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
                ["fail"] = LuaApiFunction(args => projection.Workflow.Runs.Fail(Arg<string>(args, 0), args.ElementAtOrDefault(1)))
            },
            ["items"] = new LuaTable
            {
                ["upsert"] = LuaApiFunction(args => projection.Workflow.Items.Upsert(args.ElementAtOrDefault(0))),
                ["get"] = LuaApiFunction(args => projection.Workflow.Items.Get(Arg<string>(args, 0), Arg<string>(args, 1))),
                ["getById"] = LuaApiFunction(args => projection.Workflow.Items.GetById(Arg<string>(args, 0))),
                ["query"] = LuaApiFunction(args => projection.Workflow.Items.Query(args.ElementAtOrDefault(0))),
                ["setState"] = LuaApiFunction(args => projection.Workflow.Items.SetState(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
                ["appendEvent"] = LuaApiFunction(args => projection.Workflow.Items.AppendEvent(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
                ["getEvents"] = LuaApiFunction(args => projection.Workflow.Items.GetEvents(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
                ["attachArtifact"] = LuaApiFunction(args => projection.Workflow.Items.AttachArtifact(Arg<string>(args, 0), args.ElementAtOrDefault(1))),
                ["enqueue"] = LuaApiFunction(args => projection.Workflow.Items.Enqueue(args.ElementAtOrDefault(0))),
                ["getArtifacts"] = LuaApiFunction(args => projection.Workflow.Items.GetArtifacts(Arg<string>(args, 0))),
                ["claimNext"] = LuaApiFunction(args => projection.Workflow.Items.ClaimNext(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1))),
                ["complete"] = LuaApiFunction(args => projection.Workflow.Items.Complete(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1))),
                ["fail"] = LuaApiFunction(args => projection.Workflow.Items.Fail(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1))),
                ["release"] = LuaApiFunction(args => projection.Workflow.Items.Release(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1))),
                ["retry"] = LuaApiFunction(args => projection.Workflow.Items.Retry(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1))),
                ["deadLetter"] = LuaApiFunction(args => projection.Workflow.Items.DeadLetter(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1)))
            }
        };

        g["workflows"] = new LuaTable
        {
            ["getActive"] = LuaApiFunction(args => projection.Workflows.GetActive()),
            ["list"] = LuaApiFunction(args => projection.Workflows.List()),
            ["switch"] = LuaApiFunction(args => projection.Workflows.Switch(Arg<string>(args, 0)))
        };

        _lua.Environment["g"] = g;

        _lua.Environment["btoa"] = new LuaFunction((context, _) =>
        {
            var value = context.GetArgument<string>(0);
            return new ValueTask<int>(context.Return(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))));
        });

        _lua.Environment["atob"] = new LuaFunction((context, _) =>
        {
            var value = context.GetArgument<string>(0);
            return new ValueTask<int>(context.Return(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value))));
        });

        var crypto = new LuaTable
        {
            ["randomUUID"] = new LuaFunction((context, _) => new ValueTask<int>(context.Return(Guid.NewGuid().ToString())))
        };
        _lua.Environment["crypto"] = crypto;
    }

    private static LuaFunction LuaApiFunction(Func<object?[], Task<object?>> callback) =>
        new(async (context, _) =>
        {
            var args = ReadArguments(context, 4);
            var result = await callback(args).ConfigureAwait(false);
            return result is null ? 0 : context.Return(ConvertToLuaValue(result));
        });

    private static object?[] ReadArguments(LuaFunctionExecutionContext context, int maxArguments)
    {
        var args = new List<object?>();
        for (var index = 0; index < maxArguments; index++)
        {
            if (!context.HasArgument(index))
            {
                break;
            }

            args.Add(ConvertLuaArgumentToObject(context.GetArgument<object?>(index)));
        }

        return args.ToArray();
    }

    private static object? ConvertLuaArgumentToObject(object? value)
    {
        return value switch
        {
            null => null,
            LuaValue luaValue => ConvertLuaValueToObject(luaValue),
            LuaTable table => ConvertLuaTableToObject(table),
            bool or string or double or float or int or long or decimal => value,
            _ => value
        };
    }

    private static T Arg<T>(object?[] args, int index)
    {
        if (index >= args.Length || args[index] is null)
        {
            throw new InvalidOperationException($"Argument {index + 1} is required.");
        }

        return (T)Convert.ChangeType(args[index], typeof(T), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static LuaValue ConvertToLuaValue(object? value)
    {
        if (value is null)
        {
            return LuaValue.Nil;
        }

        if (value is LuaValue luaValue)
        {
            return luaValue;
        }

        return value switch
        {
            bool typed => typed,
            byte typed => (double)typed,
            sbyte typed => (double)typed,
            short typed => (double)typed,
            ushort typed => (double)typed,
            int typed => (double)typed,
            uint typed => typed,
            long typed => (double)typed,
            ulong typed => typed,
            float typed => typed,
            double typed => typed,
            decimal typed => (double)typed,
            string typed => typed,
            System.Collections.IDictionary dictionary => ConvertDictionary(dictionary),
            System.Collections.IEnumerable enumerable when value is not string => ConvertEnumerable(enumerable),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static LuaTable ConvertDictionary(System.Collections.IDictionary dictionary)
    {
        var table = new LuaTable();
        foreach (var key in dictionary.Keys)
        {
            var tableKey = key?.ToString() ?? string.Empty;
            table[tableKey] = ConvertToLuaValue(key is null ? null : dictionary[key]);
        }

        return table;
    }

    private static LuaTable ConvertEnumerable(System.Collections.IEnumerable enumerable)
    {
        var table = new LuaTable();
        var index = 1;
        foreach (var item in enumerable)
        {
            table[index++] = ConvertToLuaValue(item);
        }

        return table;
    }

    private static object? ConvertLuaValueToObject(LuaValue value)
    {
        return value.Type switch
        {
            LuaValueType.Nil => null,
            LuaValueType.Boolean => value.Read<bool>(),
            LuaValueType.Number => value.Read<double>(),
            LuaValueType.String => value.Read<string>(),
            LuaValueType.Table => ConvertLuaTableToObject(value.Read<LuaTable>()),
            _ => value.ToString()
        };
    }

    private static object? ConvertLuaTableToObject(LuaTable table)
    {
        var itemCount = table.Count();
        if (itemCount > 0 && IsArray(table, itemCount))
        {
            var list = new List<object?>();
            for (var index = 1; index <= itemCount; index++)
            {
                list.Add(ConvertLuaValueToObject(table[index]));
            }

            return list;
        }

        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in table)
        {
            var key = pair.Key.Type == LuaValueType.String
                ? pair.Key.Read<string>()
                : pair.Key.ToString();
            dictionary[key] = ConvertLuaValueToObject(pair.Value);
        }

        return dictionary;
    }

    private static bool IsArray(LuaTable table, int count)
    {
        for (var index = 1; index <= count; index++)
        {
            if (table[index].Type == LuaValueType.Nil)
            {
                return false;
            }
        }

        return true;
    }
}
