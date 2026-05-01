using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptEngineFactoryTests
{
    [Fact]
    public void Register_marks_language_as_registered()
    {
        var factory = new ScriptEngineFactory();

        factory.Register(ScriptLanguage.JavaScript, _ => new FakeScriptEngine());

        Assert.True(factory.IsRegistered(ScriptLanguage.JavaScript));
        Assert.False(factory.IsRegistered(ScriptLanguage.Lua));
    }

    [Fact]
    public void Create_returns_engine_from_registered_factory()
    {
        var factory = new ScriptEngineFactory();
        var engine = new FakeScriptEngine();

        factory.Register(ScriptLanguage.Lua, _ => engine);

        Assert.Same(engine, factory.Create(ScriptLanguage.Lua));
    }

    [Fact]
    public void Create_passes_creation_context_to_registered_factory()
    {
        var factory = new ScriptEngineFactory();
        var hostContext = new ScriptHostContext { Logger = new FakeScriptLogger() };
        ScriptEngineCreationContext? capturedContext = null;
        factory.Register(
            ScriptLanguage.TypeScript,
            context =>
            {
                capturedContext = context;
                return new FakeScriptEngine();
            });

        factory.Create(new ScriptEngineCreationContext
        {
            Language = ScriptLanguage.TypeScript,
            Name = "script.ts",
            HostContext = hostContext
        });

        Assert.NotNull(capturedContext);
        Assert.Equal(ScriptLanguage.TypeScript, capturedContext.Language);
        Assert.Equal("script.ts", capturedContext.Name);
        Assert.Same(hostContext, capturedContext.HostContext);
    }

    [Fact]
    public void Register_replaces_existing_registration_for_language()
    {
        var factory = new ScriptEngineFactory();
        var first = new FakeScriptEngine();
        var second = new FakeScriptEngine();
        factory.Register(ScriptLanguage.Janet, _ => first);

        factory.Register(ScriptLanguage.Janet, _ => second);

        Assert.Same(second, factory.Create(ScriptLanguage.Janet));
    }

    [Fact]
    public void Register_rejects_unknown_language()
    {
        var factory = new ScriptEngineFactory();

        Assert.Throws<ArgumentException>(() =>
            factory.Register(ScriptLanguage.Unknown, _ => new FakeScriptEngine()));
    }

    [Fact]
    public void Register_rejects_null_factory()
    {
        var factory = new ScriptEngineFactory();

        Assert.Throws<ArgumentNullException>(() =>
            factory.Register(ScriptLanguage.JavaScript, null!));
    }

    [Fact]
    public void Create_rejects_unknown_language()
    {
        var factory = new ScriptEngineFactory();

        var exception = Assert.Throws<NotSupportedException>(() =>
            factory.Create(ScriptLanguage.Unknown));
        Assert.Contains("unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_rejects_unregistered_language()
    {
        var factory = new ScriptEngineFactory();

        var exception = Assert.Throws<NotSupportedException>(() =>
            factory.Create(ScriptLanguage.JavaScript));
        Assert.Contains(nameof(ScriptLanguage.JavaScript), exception.Message);
    }

    private sealed class FakeScriptEngine : IScriptEngine
    {
        public Task<ScriptExecutionResult> ExecuteAsync(
            ScriptExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ScriptExecutionResult.Succeeded());

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeScriptLogger : IScriptLogger
    {
        public void Log(ScriptLogEntry entry)
        {
        }
    }
}
