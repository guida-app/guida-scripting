using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptHostContextTests
{
    [Fact]
    public void Empty_has_no_logger()
    {
        Assert.Null(ScriptHostContext.Empty.Logger);
        Assert.Same(ScriptExecutionInfo.Empty, ScriptHostContext.Empty.Execution);
    }

    [Fact]
    public void ScriptExecutionInfo_empty_has_default_user_origin()
    {
        Assert.Null(ScriptExecutionInfo.Empty.TaskId);
        Assert.Equal(ScriptTaskOrigin.User, ScriptExecutionInfo.Empty.Origin);
    }

    [Fact]
    public void ScriptLogEntry_defaults_to_information_with_empty_message()
    {
        var before = DateTimeOffset.UtcNow;

        var entry = new ScriptLogEntry();

        Assert.Equal(ScriptLogLevel.Information, entry.Level);
        Assert.Equal(string.Empty, entry.Message);
        Assert.Null(entry.Category);
        Assert.Null(entry.TaskId);
        Assert.True(entry.Timestamp >= before);
        Assert.True(entry.Timestamp <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Logger_receives_log_entries()
    {
        var logger = new CapturingLogger();
        var context = new ScriptHostContext { Logger = logger };
        var entry = new ScriptLogEntry
        {
            Level = ScriptLogLevel.Warning,
            Message = "careful",
            Category = "runtime",
            TaskId = "task-1"
        };

        context.Logger!.Log(entry);

        var captured = Assert.Single(logger.Entries);
        Assert.Equal(ScriptLogLevel.Warning, captured.Level);
        Assert.Equal("careful", captured.Message);
        Assert.Equal("runtime", captured.Category);
        Assert.Equal("task-1", captured.TaskId);
    }

    private sealed class CapturingLogger : IScriptLogger
    {
        public List<ScriptLogEntry> Entries { get; } = [];

        public void Log(ScriptLogEntry entry) => Entries.Add(entry);
    }
}
