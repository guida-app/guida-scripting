using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptHostContextTests
{
    [Fact]
    public void Empty_has_no_logger()
    {
        Assert.Null(ScriptHostContext.Empty.Logger);
        Assert.Same(ScriptExecutionInfo.Empty, ScriptHostContext.Empty.Execution);
        Assert.Same(ScriptHostCapabilities.Empty, ScriptHostContext.Empty.Capabilities);
        Assert.False(ScriptHostContext.Empty.TryGetCapability<IFakeCapability>(out _));
    }

    [Fact]
    public void ScriptExecutionInfo_empty_has_default_user_origin_and_trusted_policy()
    {
        Assert.Null(ScriptExecutionInfo.Empty.TaskId);
        Assert.Equal(ScriptTaskOrigin.User, ScriptExecutionInfo.Empty.Origin);
        Assert.Equal(ScriptExecutionPolicy.Trusted, ScriptExecutionInfo.Empty.Policy);
    }

    [Fact]
    public void WithCapability_preserves_logger_and_execution_metadata()
    {
        var logger = new CapturingLogger();
        var execution = new ScriptExecutionInfo
        {
            TaskId = "task-1",
            Origin = ScriptTaskOrigin.Host
        };
        var capability = new FakeCapability("workspace");
        var context = new ScriptHostContext
        {
            Logger = logger,
            Execution = execution
        };

        var updated = context.WithCapability<IFakeCapability>(capability);

        Assert.Same(logger, updated.Logger);
        Assert.Same(execution, updated.Execution);
        Assert.Same(capability, updated.GetCapability<IFakeCapability>());
    }

    [Fact]
    public void Capabilities_retrieve_by_registered_public_type()
    {
        var capability = new FakeCapability("workspace");
        var context = ScriptHostContext.Empty.WithCapability<IFakeCapability>(capability);

        Assert.True(context.TryGetCapability<IFakeCapability>(out var found));
        Assert.Same(capability, found);
        Assert.Same(capability, context.GetCapability<IFakeCapability>());
        Assert.Null(context.GetCapability<FakeCapability>());
    }

    [Fact]
    public void Capabilities_return_false_or_null_for_missing_capability()
    {
        var capabilities = ScriptHostCapabilities.Empty;

        Assert.False(capabilities.TryGet<IFakeCapability>(out var found));
        Assert.Null(found);
        Assert.Null(capabilities.Get<IFakeCapability>());
    }

    [Fact]
    public void Capabilities_replace_existing_capability_for_same_type()
    {
        var first = new FakeCapability("first");
        var second = new FakeCapability("second");
        var capabilities = ScriptHostCapabilities.Empty
            .Set<IFakeCapability>(first)
            .Set<IFakeCapability>(second);

        Assert.Same(second, capabilities.Get<IFakeCapability>());
    }

    [Fact]
    public void Capabilities_remove_registered_capability()
    {
        var capabilities = ScriptHostCapabilities.Empty
            .Set<IFakeCapability>(new FakeCapability("workspace"))
            .Remove<IFakeCapability>();

        Assert.Same(ScriptHostCapabilities.Empty, capabilities);
        Assert.False(capabilities.TryGet<IFakeCapability>(out _));
    }

    [Fact]
    public void Capabilities_reject_null_capability()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ScriptHostCapabilities.Empty.Set<IFakeCapability>(null!));
    }

    [Fact]
    public void ScriptCapabilityUnavailable_creates_failed_execution_result()
    {
        var unavailable = ScriptCapabilityUnavailable.For<IFakeCapability>();
        var capabilityName = typeof(IFakeCapability).FullName!;

        Assert.Equal(capabilityName, unavailable.CapabilityName);
        Assert.Equal($"Host capability '{capabilityName}' is unavailable.", unavailable.Message);

        var result = unavailable.ToExecutionResult();

        Assert.False(result.Success);
        Assert.Equal(unavailable.Message, result.Error);
        Assert.Empty(result.ReturnValues);
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

    private interface IFakeCapability : IScriptHostCapability
    {
    }

    private sealed record FakeCapability(string Name) : IFakeCapability;
}
