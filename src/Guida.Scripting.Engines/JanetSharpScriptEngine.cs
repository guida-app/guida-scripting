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
