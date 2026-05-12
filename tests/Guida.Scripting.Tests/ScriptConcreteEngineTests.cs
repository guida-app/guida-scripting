using System.Text.Json;
using Guida.Scripting;
using Guida.Scripting.Engines;

namespace Guida.Scripting.Tests;

public sealed class ScriptConcreteEngineTests
{
    [Fact]
    public void RegisterStandardEngines_registers_all_public_engine_languages()
    {
        var factory = new ScriptEngineFactory().RegisterStandardEngines();

        Assert.True(factory.IsRegistered(ScriptLanguage.JavaScript));
        Assert.True(factory.IsRegistered(ScriptLanguage.TypeScript));
        Assert.True(factory.IsRegistered(ScriptLanguage.Lua));
        Assert.True(factory.IsRegistered(ScriptLanguage.Janet));
    }

    [Theory]
    [MemberData(nameof(BasicScripts))]
    public async Task Concrete_engines_execute_basic_scripts(
        ScriptLanguage language,
        string source,
        object expected)
    {
        using var engine = CreateEngine(language, ScriptHostContext.Empty);

        var result = await engine.ExecuteAsync(new ScriptExecutionRequest
        {
            Language = language,
            Source = source,
            Name = $"basic.{ExtensionFor(language)}"
        });

        Assert.True(result.Success, result.Error);
        AssertSingleValue(result, expected);
    }

    [Theory]
    [MemberData(nameof(BasicScripts))]
    public async Task Task_manager_runs_concrete_engines_through_common_runtime(
        ScriptLanguage language,
        string source,
        object expected)
    {
        var manager = new ScriptTaskManager(new ScriptEngineFactory().RegisterStandardEngines());

        var task = await manager.StartAsync(new ScriptExecutionRequest
        {
            Language = language,
            Source = source,
            Name = $"task.{ExtensionFor(language)}"
        });

        Assert.Equal(ScriptTaskStatus.Completed, task.Status);
        AssertSingleValue(task.ReturnValues, expected);
    }

    [Theory]
    [MemberData(nameof(LogScripts))]
    public async Task Concrete_engines_expose_standard_logging_host_call(
        ScriptLanguage language,
        string source)
    {
        var logger = new CapturingLogger();
        using var engine = CreateEngine(language, new ScriptHostContext { Logger = logger });

        var result = await engine.ExecuteAsync(new ScriptExecutionRequest
        {
            Language = language,
            Source = source,
            Name = $"log.{ExtensionFor(language)}",
            HostContext = new ScriptHostContext { Logger = logger }
        });

        Assert.True(result.Success, result.Error);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("hello", entry.Message);
    }

    [Theory]
    [MemberData(nameof(ErrorScripts))]
    public async Task Concrete_engines_map_script_exceptions_to_failed_results(
        ScriptLanguage language,
        string source)
    {
        using var engine = CreateEngine(language, ScriptHostContext.Empty);

        var result = await engine.ExecuteAsync(new ScriptExecutionRequest
        {
            Language = language,
            Source = source,
            Name = $"error.{ExtensionFor(language)}"
        });

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Contains("boom", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ScriptLanguage.JavaScript, "async function main() { await g.wait(1000); return 42; }")]
    [InlineData(ScriptLanguage.Lua, "g.wait(1000)\nreturn 42")]
    [InlineData(ScriptLanguage.Janet, "(do (g.wait 1000) 42)")]
    public async Task Concrete_engines_map_timeouts_to_timed_out_results(
        ScriptLanguage language,
        string source)
    {
        using var engine = CreateEngine(language, ScriptHostContext.Empty);

        var result = await engine.ExecuteAsync(new ScriptExecutionRequest
        {
            Language = language,
            Source = source,
            Name = $"timeout.{ExtensionFor(language)}",
            Timeout = TimeSpan.FromMilliseconds(25)
        });

        Assert.True(result.IsTimedOut, result.Error);
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(ScriptLanguage.JavaScript, "async function main() { await g.wait(1000); return 42; }")]
    [InlineData(ScriptLanguage.Lua, "g.wait(1000)\nreturn 42")]
    [InlineData(ScriptLanguage.Janet, "(do (g.wait 1000) 42)")]
    public async Task Concrete_engines_map_caller_cancellation_to_canceled_results(
        ScriptLanguage language,
        string source)
    {
        using var engine = CreateEngine(language, ScriptHostContext.Empty);
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        var result = await engine.ExecuteAsync(
            new ScriptExecutionRequest
            {
                Language = language,
                Source = source,
                Name = $"cancel.{ExtensionFor(language)}"
            },
            cancellationTokenSource.Token);

        Assert.True(result.IsCanceled, result.Error);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Stop_cancels_running_JavaScript_task_through_task_manager()
    {
        var manager = new ScriptTaskManager(new ScriptEngineFactory().RegisterClearScriptEngine());
        var handle = manager.Start(new ScriptExecutionRequest
        {
            Language = ScriptLanguage.JavaScript,
            Source = "async function main() { await g.wait(10000); return 42; }",
            Name = "stop.js"
        });

        await WaitForRunningTaskAsync(manager, handle.Id);
        Assert.True(manager.Stop(handle.Id));

        var final = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ScriptTaskStatus.Canceled, final.Status);
    }

    [Fact]
    public async Task JavaScript_engine_loads_standard_imports_from_document_provider()
    {
        var provider = new FakeDocumentProvider
        {
            Documents =
            {
                ["lib.js"] = "export const value = 41;"
            }
        };
        var hostContext = ScriptHostContext.Empty.WithCapability<IScriptDocumentProvider>(provider);
        using var engine = CreateEngine(ScriptLanguage.JavaScript, hostContext);

        var result = await engine.ExecuteAsync(new ScriptExecutionRequest
        {
            Language = ScriptLanguage.JavaScript,
            Source = "import { value } from \"lib.js\";\nfunction main() { return value + 1; }",
            Name = "main.js",
            HostContext = hostContext
        });

        Assert.True(result.Success, result.Error);
        AssertSingleValue(result, 42);
    }

    [Fact]
    public async Task JavaScript_engine_maps_document_provider_errors_to_failed_results()
    {
        var provider = new FakeDocumentProvider();
        var hostContext = ScriptHostContext.Empty.WithCapability<IScriptDocumentProvider>(provider);
        using var engine = CreateEngine(ScriptLanguage.JavaScript, hostContext);

        var result = await engine.ExecuteAsync(new ScriptExecutionRequest
        {
            Language = ScriptLanguage.JavaScript,
            Source = "import { value } from \"missing.js\";\nfunction main() { return value; }",
            Name = "missing-import.js",
            HostContext = hostContext
        });

        Assert.False(result.Success);
        Assert.Contains("missing.js", result.Error);
    }

    [Fact]
    public async Task JavaScript_engine_snapshots_object_return_values()
    {
        using var engine = CreateEngine(ScriptLanguage.JavaScript, ScriptHostContext.Empty);

        var result = await engine.ExecuteAsync(new ScriptExecutionRequest
        {
            Language = ScriptLanguage.JavaScript,
            Source = "({ name: 'answer', value: 42 })",
            Name = "object.js"
        });

        Assert.True(result.Success, result.Error);
        var json = Assert.IsType<JsonElement>(Assert.Single(result.ReturnValues));
        Assert.Equal("answer", json.GetProperty("name").GetString());
        Assert.Equal(42, json.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task JavaScript_engine_executes_async_main()
    {
        using var engine = CreateEngine(ScriptLanguage.JavaScript, ScriptHostContext.Empty);

        var result = await engine.ExecuteAsync(new ScriptExecutionRequest
        {
            Language = ScriptLanguage.JavaScript,
            Source = "async function main() { await g.wait(1); return 'ok'; }",
            Name = "async.js"
        });

        Assert.True(result.Success, result.Error);
        AssertSingleValue(result, "ok");
    }

    public static TheoryData<ScriptLanguage, string, object> BasicScripts() =>
        new()
        {
            { ScriptLanguage.JavaScript, "1 + 2", 3 },
            { ScriptLanguage.Lua, "return 1 + 2", 3 },
            { ScriptLanguage.Janet, "(+ 1 2)", 3 }
        };

    public static TheoryData<ScriptLanguage, string> LogScripts() =>
        new()
        {
            { ScriptLanguage.JavaScript, "g.log('hello'); 'done';" },
            { ScriptLanguage.Lua, "g.log('hello')\nreturn 'done'" },
            { ScriptLanguage.Janet, "(do (g.log \"hello\") \"done\")" }
        };

    public static TheoryData<ScriptLanguage, string> ErrorScripts() =>
        new()
        {
            { ScriptLanguage.JavaScript, "throw new Error('boom')" },
            { ScriptLanguage.Lua, "error('boom')" },
            { ScriptLanguage.Janet, "(error \"boom\")" }
        };

    private static IScriptEngine CreateEngine(
        ScriptLanguage language,
        ScriptHostContext hostContext)
    {
        var factory = new ScriptEngineFactory().RegisterStandardEngines();
        return factory.Create(new ScriptEngineCreationContext
        {
            Language = language,
            Name = $"engine.{ExtensionFor(language)}",
            HostContext = hostContext
        });
    }

    private static async Task WaitForRunningTaskAsync(ScriptTaskManager manager, string taskId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            if (manager.GetTask(taskId)?.Status == ScriptTaskStatus.Running)
            {
                return;
            }

            await Task.Delay(10, timeout.Token);
        }

        throw new TimeoutException("The task did not reach the running state.");
    }

    private static string ExtensionFor(ScriptLanguage language) =>
        language switch
        {
            ScriptLanguage.JavaScript => "js",
            ScriptLanguage.TypeScript => "ts",
            ScriptLanguage.Lua => "lua",
            ScriptLanguage.Janet => "janet",
            _ => "txt"
        };

    private static void AssertSingleValue(ScriptExecutionResult result, object expected) =>
        AssertSingleValue(result.ReturnValues, expected);

    private static void AssertSingleValue(IReadOnlyList<object?> values, object expected)
    {
        var actual = Assert.Single(values);
        if (expected is int expectedInteger && actual is double actualDouble)
        {
            Assert.Equal(expectedInteger, actualDouble);
            return;
        }

        Assert.Equal(expected, actual);
    }

    private sealed class CapturingLogger : IScriptLogger
    {
        public List<ScriptLogEntry> Entries { get; } = [];

        public void Log(ScriptLogEntry entry) => Entries.Add(entry);
    }

    private sealed class FakeDocumentProvider : IScriptDocumentProvider
    {
        public Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);

        public Task<ScriptDocumentResult<ScriptDocument>> LoadAsync(
            string documentId,
            ScriptDocumentLoadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (!Documents.TryGetValue(documentId, out var source))
            {
                return Task.FromResult(ScriptDocumentResult<ScriptDocument>.Failed(
                    new ScriptDocumentError(
                        ScriptDocumentErrorCode.NotFound,
                        documentId,
                        $"Document '{documentId}' was not found.")));
            }

            return Task.FromResult(ScriptDocumentResult<ScriptDocument>.Succeeded(new ScriptDocument
            {
                Id = documentId,
                Name = documentId,
                Source = source,
                Language = options?.Language ?? ScriptLanguage.JavaScript
            }));
        }
    }
}
