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
