using System.Diagnostics;

namespace Guida.Scripting.Engines;

internal static class ScriptEngineResultHelpers
{
    public static async Task<ScriptExecutionResult> RunWithTiming(
        Func<CancellationToken, Task<ScriptExecutionResult>> execute,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : null;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts?.Token ?? CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await execute(linkedCts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return result with { Elapsed = stopwatch.Elapsed };
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return ScriptExecutionResult.TimedOut(timeout!.Value) with { Elapsed = stopwatch.Elapsed };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ScriptExecutionResult.Canceled("Script execution canceled.") with { Elapsed = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ScriptExecutionResult.Failed(ex.Message, ex) with { Elapsed = stopwatch.Elapsed };
        }
    }

    public static void Log(ScriptHostContext context, ScriptLogLevel level, string message, string category)
    {
        context.Logger?.Log(new ScriptLogEntry
        {
            Level = level,
            Message = message,
            Category = category,
            TaskId = context.Execution.TaskId
        });
    }
}
