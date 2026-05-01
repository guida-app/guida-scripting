using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptExecutionResultTests
{
    [Fact]
    public void Succeeded_sets_success_and_return_values()
    {
        var result = ScriptExecutionResult.Succeeded("value", 42);

        Assert.True(result.Success);
        Assert.Equal(["value", 42], result.ReturnValues);
        Assert.False(result.IsCanceled);
        Assert.False(result.IsTimedOut);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failed_sets_error_and_exception_metadata()
    {
        var exception = new InvalidOperationException("bad state");

        var result = ScriptExecutionResult.Failed("Execution failed.", exception);

        Assert.False(result.Success);
        Assert.Equal("Execution failed.", result.Error);
        Assert.Equal(typeof(InvalidOperationException).FullName, result.ExceptionType);
        Assert.False(result.IsCanceled);
        Assert.False(result.IsTimedOut);
    }

    [Fact]
    public void Canceled_sets_cancellation_flag()
    {
        var result = ScriptExecutionResult.Canceled("Stopped.");

        Assert.False(result.Success);
        Assert.True(result.IsCanceled);
        Assert.False(result.IsTimedOut);
        Assert.Equal("Stopped.", result.Error);
    }

    [Fact]
    public void TimedOut_sets_timeout_flag_and_message()
    {
        var timeout = TimeSpan.FromSeconds(5);

        var result = ScriptExecutionResult.TimedOut(timeout);

        Assert.False(result.Success);
        Assert.False(result.IsCanceled);
        Assert.True(result.IsTimedOut);
        Assert.Contains(timeout.ToString(), result.Error);
    }
}
