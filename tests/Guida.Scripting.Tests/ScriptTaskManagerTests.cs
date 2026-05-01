using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptTaskManagerTests
{
    [Fact]
    public async Task Start_returns_running_handle_before_engine_completion()
    {
        var engine = new FakeScriptEngine();
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);
        ScriptTaskRecord? started = null;
        manager.TaskStarted += (_, task) => started = task;

        var handle = manager.Start(
            new ScriptExecutionRequest
            {
                Source = "return 42",
                Language = ScriptLanguage.JavaScript,
                Name = "answer.js"
            },
            new ScriptTaskStartOptions { Name = "Answer" });

        Assert.NotEqual(string.Empty, handle.Id);
        Assert.Equal(handle.Id, handle.InitialRecord.Id);
        Assert.Equal("Answer", handle.InitialRecord.Name);
        Assert.Equal(ScriptTaskStatus.Running, handle.InitialRecord.Status);
        Assert.False(handle.Completion.IsCompleted);
        Assert.NotNull(started);
        Assert.Equal(handle.Id, started.Id);

        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(handle.Id, Assert.Single(manager.GetTasks()).Id);

        engine.Complete(ScriptExecutionResult.Succeeded("ok"));
        var final = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(handle.Id, final.Id);
        Assert.Equal(ScriptTaskStatus.Completed, final.Status);
        Assert.Equal(["ok"], final.ReturnValues);
    }

    [Fact]
    public async Task Start_completion_fires_TaskCompleted()
    {
        var engine = new FakeScriptEngine();
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);
        ScriptTaskRecord? completed = null;
        manager.TaskCompleted += (_, task) => completed = task;

        var handle = manager.Start(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        engine.Complete(ScriptExecutionResult.Succeeded("done"));
        var final = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(completed);
        Assert.Equal(final.Id, completed.Id);
        Assert.Equal(ScriptTaskStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task Stop_can_cancel_task_immediately_after_Start_returns()
    {
        var engine = new FakeScriptEngine
        {
            ExecuteAsyncHandler = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return ScriptExecutionResult.Succeeded();
            }
        };
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);

        var handle = manager.Start(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });

        Assert.True(manager.Stop(handle.Id));
        var final = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ScriptTaskStatus.Canceled, final.Status);
    }

    [Fact]
    public async Task StopAll_cancels_tasks_started_with_Start()
    {
        var engines = new Queue<FakeScriptEngine>(
        [
            new FakeScriptEngine
            {
                ExecuteAsyncHandler = async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return ScriptExecutionResult.Succeeded();
                }
            },
            new FakeScriptEngine
            {
                ExecuteAsyncHandler = async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return ScriptExecutionResult.Succeeded();
                }
            }
        ]);
        var factory = new ScriptEngineFactory();
        factory.Register(ScriptLanguage.JavaScript, _ => engines.Dequeue());
        var manager = new ScriptTaskManager(factory);

        var first = manager.Start(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });
        var second = manager.Start(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });

        Assert.Equal(2, manager.StopAll());

        Assert.Equal(ScriptTaskStatus.Canceled, (await first.Completion.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.Equal(ScriptTaskStatus.Canceled, (await second.Completion.WaitAsync(TimeSpan.FromSeconds(5))).Status);
    }

    [Fact]
    public async Task StartAsync_tracks_running_task_and_completed_result()
    {
        var engine = new FakeScriptEngine();
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);
        ScriptTaskRecord? started = null;
        ScriptTaskRecord? completed = null;
        manager.TaskStarted += (_, task) => started = task;
        manager.TaskCompleted += (_, task) => completed = task;

        var task = manager.StartAsync(
            new ScriptExecutionRequest
            {
                Source = "return 42",
                Language = ScriptLanguage.JavaScript,
                Name = "answer.js"
            },
            new ScriptTaskStartOptions
            {
                Name = "Answer",
                Origin = ScriptTaskOrigin.Host,
                Timeout = TimeSpan.FromSeconds(10)
            });

        var executedRequest = await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var running = Assert.Single(manager.GetTasks());

        Assert.NotNull(started);
        Assert.Equal(started.Id, running.Id);
        Assert.Equal("Answer", running.Name);
        Assert.Equal(ScriptTaskOrigin.Host, running.Origin);
        Assert.Equal(ScriptLanguage.JavaScript, running.Language);
        Assert.Equal(ScriptTaskStatus.Running, running.Status);
        Assert.Equal("answer.js", running.ScriptName);
        Assert.Equal(TimeSpan.FromSeconds(10), running.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(10), executedRequest.Timeout);

        engine.Complete(ScriptExecutionResult.Succeeded("ok", 42));

        var final = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(completed);
        Assert.Equal(final.Id, completed.Id);
        Assert.Equal(final.Status, completed.Status);
        Assert.Equal(ScriptTaskStatus.Completed, final.Status);
        Assert.Equal(["ok", 42], final.ReturnValues);
        Assert.NotNull(final.EndedAt);
        Assert.NotNull(final.Duration);
        Assert.True(engine.Disposed);

        var lookup = manager.GetTask(final.Id);
        Assert.NotNull(lookup);
        Assert.Equal(final.Id, lookup.Id);
        Assert.Equal(final.Status, lookup.Status);
        Assert.Equal(final.ReturnValues, lookup.ReturnValues);
    }

    [Fact]
    public async Task StartAsync_still_returns_final_task_record()
    {
        var engine = new FakeScriptEngine();
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);

        var task = manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        engine.Complete(ScriptExecutionResult.Succeeded("final"));
        var final = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ScriptTaskStatus.Completed, final.Status);
        Assert.Equal(["final"], final.ReturnValues);
    }

    [Fact]
    public async Task Start_returns_failed_completion_for_unregistered_language()
    {
        var manager = new ScriptTaskManager(new ScriptEngineFactory());

        var handle = manager.Start(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });
        var final = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(handle.Id, final.Id);
        Assert.Equal(ScriptTaskStatus.Failed, final.Status);
        Assert.Contains("No script engine is registered", final.Error);
    }

    [Fact]
    public async Task StartAsync_detects_language_from_script_name()
    {
        var engine = new FakeScriptEngine(ScriptExecutionResult.Succeeded("lua"));
        var manager = CreateManager(engine, ScriptLanguage.Lua);

        var final = await manager.StartAsync(new ScriptExecutionRequest { Name = "job.lua" });

        Assert.Equal(ScriptTaskStatus.Completed, final.Status);
        Assert.Equal(ScriptLanguage.Lua, final.Language);
    }

    [Fact]
    public async Task StartAsync_uses_language_override()
    {
        var engine = new FakeScriptEngine(ScriptExecutionResult.Succeeded());
        var manager = CreateManager(engine, ScriptLanguage.TypeScript);

        var final = await manager.StartAsync(
            new ScriptExecutionRequest
            {
                Name = "job.lua",
                Language = ScriptLanguage.Lua
            },
            new ScriptTaskStartOptions { Language = ScriptLanguage.TypeScript });

        Assert.Equal(ScriptTaskStatus.Completed, final.Status);
        Assert.Equal(ScriptLanguage.TypeScript, final.Language);
    }

    [Fact]
    public async Task StartAsync_passes_host_context_to_engine_creation()
    {
        var engine = new FakeScriptEngine(ScriptExecutionResult.Succeeded());
        var capability = new FakeCapability();
        var hostContext = new ScriptHostContext { Logger = new FakeScriptLogger() }
            .WithCapability<IFakeCapability>(capability);
        ScriptEngineCreationContext? capturedContext = null;
        var factory = new ScriptEngineFactory();
        factory.Register(
            ScriptLanguage.JavaScript,
            context =>
            {
                capturedContext = context;
                return engine;
            });
        var manager = new ScriptTaskManager(factory);

        var final = await manager.StartAsync(
            new ScriptExecutionRequest
            {
                Language = ScriptLanguage.JavaScript,
                HostContext = hostContext
            },
            new ScriptTaskStartOptions { Origin = ScriptTaskOrigin.External });
        var executedRequest = await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ScriptTaskStatus.Completed, final.Status);
        Assert.NotNull(capturedContext);
        Assert.Same(hostContext.Logger, capturedContext.HostContext.Logger);
        Assert.Same(capability, capturedContext.HostContext.GetCapability<IFakeCapability>());
        Assert.Equal(final.Id, capturedContext.HostContext.Execution.TaskId);
        Assert.Equal(ScriptTaskOrigin.External, capturedContext.HostContext.Execution.Origin);
        Assert.Same(hostContext.Logger, executedRequest.HostContext.Logger);
        Assert.Same(capability, executedRequest.HostContext.GetCapability<IFakeCapability>());
        Assert.Equal(final.Id, executedRequest.HostContext.Execution.TaskId);
        Assert.Equal(
            capturedContext.HostContext.Execution,
            executedRequest.HostContext.Execution);
    }

    [Fact]
    public async Task StartAsync_maps_failed_engine_result()
    {
        var engine = new FakeScriptEngine(ScriptExecutionResult.Failed("bad script"));
        var manager = CreateManager(
            engine,
            ScriptLanguage.JavaScript);

        var final = await manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });

        Assert.Equal(ScriptTaskStatus.Failed, final.Status);
        Assert.Equal("bad script", final.Error);
        Assert.True(engine.Disposed);
    }

    [Fact]
    public async Task StartAsync_maps_timed_out_engine_result()
    {
        var timeout = TimeSpan.FromSeconds(1);
        var engine = new FakeScriptEngine(ScriptExecutionResult.TimedOut(timeout));
        var manager = CreateManager(
            engine,
            ScriptLanguage.JavaScript);

        var final = await manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });

        Assert.Equal(ScriptTaskStatus.TimedOut, final.Status);
        Assert.Contains(timeout.ToString(), final.Error);
        Assert.True(engine.Disposed);
    }

    [Fact]
    public async Task StartAsync_does_not_enforce_timeout_when_engine_returns_success()
    {
        var engine = new FakeScriptEngine
        {
            ExecuteAsyncHandler = async (_, _) =>
            {
                await Task.Delay(75);
                return ScriptExecutionResult.Succeeded("late");
            }
        };
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);

        var final = await manager.StartAsync(
            new ScriptExecutionRequest
            {
                Language = ScriptLanguage.JavaScript,
                Timeout = TimeSpan.FromMilliseconds(1)
            });

        Assert.Equal(ScriptTaskStatus.Completed, final.Status);
        Assert.Equal(["late"], final.ReturnValues);
    }

    [Fact]
    public async Task StartAsync_maps_canceled_result()
    {
        var engine = new FakeScriptEngine(ScriptExecutionResult.Canceled("stopped"));
        var manager = CreateManager(
            engine,
            ScriptLanguage.JavaScript);

        var final = await manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });

        Assert.Equal(ScriptTaskStatus.Canceled, final.Status);
        Assert.Equal("stopped", final.Error);
        Assert.True(engine.Disposed);
    }

    [Fact]
    public async Task StartAsync_maps_operation_canceled_exception()
    {
        var engine = new FakeScriptEngine
        {
            ExecuteAsyncHandler = (_, cancellationToken) => Task.FromCanceled<ScriptExecutionResult>(cancellationToken)
        };
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        engine.ExecuteAsyncHandler = (_, _) => throw new OperationCanceledException(cancellationTokenSource.Token);

        var final = await manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });

        Assert.Equal(ScriptTaskStatus.Canceled, final.Status);
        Assert.True(engine.Disposed);
    }

    [Fact]
    public async Task StartAsync_maps_engine_exception_to_failed_task_and_disposes_engine()
    {
        var engine = new FakeScriptEngine
        {
            ExecuteAsyncHandler = (_, _) => throw new InvalidOperationException("engine failed")
        };
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);

        var final = await manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });

        Assert.Equal(ScriptTaskStatus.Failed, final.Status);
        Assert.Equal("engine failed", final.Error);
        Assert.True(engine.Disposed);
    }

    [Fact]
    public async Task StartAsync_maps_factory_exception_to_failed_task()
    {
        var factory = new ScriptEngineFactory();
        factory.Register(ScriptLanguage.JavaScript, _ => throw new InvalidOperationException("factory failed"));
        var manager = new ScriptTaskManager(factory);
        ScriptTaskRecord? completed = null;
        manager.TaskCompleted += (_, task) => completed = task;

        var final = await manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });

        Assert.Equal(ScriptTaskStatus.Failed, final.Status);
        Assert.Equal("factory failed", final.Error);
        Assert.NotNull(completed);
        Assert.Equal(final.Id, completed.Id);
    }

    [Fact]
    public async Task StartAsync_returns_failed_task_for_unregistered_language()
    {
        var manager = new ScriptTaskManager(new ScriptEngineFactory());

        var final = await manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });

        Assert.Equal(ScriptTaskStatus.Failed, final.Status);
        Assert.Contains("No script engine is registered", final.Error);
    }

    [Fact]
    public async Task StartAsync_returns_failed_task_for_unknown_language()
    {
        var manager = new ScriptTaskManager(new ScriptEngineFactory());

        var final = await manager.StartAsync(new ScriptExecutionRequest());

        Assert.Equal(ScriptTaskStatus.Failed, final.Status);
        Assert.Contains("determine", final.Error);
    }

    [Fact]
    public void Stop_returns_false_for_unknown_task()
    {
        var manager = new ScriptTaskManager(new ScriptEngineFactory());

        Assert.False(manager.Stop("missing"));
    }

    [Fact]
    public void Stop_returns_false_for_completed_task()
    {
        var manager = new ScriptTaskManager(new ScriptEngineFactory());
        var external = manager.RegisterExternalTask("external");
        Assert.True(manager.CompleteExternalTask(external.Id, ScriptTaskStatus.Completed));

        Assert.False(manager.Stop(external.Id));
    }

    [Fact]
    public async Task Stop_cancels_running_task_and_calls_engine_stop()
    {
        var engine = new FakeScriptEngine
        {
            ExecuteAsyncHandler = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return ScriptExecutionResult.Succeeded();
            }
        };
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);
        var task = manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var running = Assert.Single(manager.GetTasks());

        Assert.True(manager.Stop(running.Id));
        var final = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ScriptTaskStatus.Canceled, final.Status);
        Assert.Equal(1, engine.StopCalls);
        Assert.True(engine.Disposed);
    }

    [Fact]
    public async Task Stop_maps_canceled_token_to_canceled_even_when_engine_returns_success()
    {
        var engine = new FakeScriptEngine
        {
            ExecuteAsyncHandler = async (_, cancellationToken) =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Yield();
                }

                return ScriptExecutionResult.Succeeded("ignored");
            }
        };
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);
        var task = manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var running = Assert.Single(manager.GetTasks());

        Assert.True(manager.Stop(running.Id));
        var final = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ScriptTaskStatus.Canceled, final.Status);
    }

    [Fact]
    public async Task StopAll_cancels_all_running_tasks()
    {
        var engines = new Queue<FakeScriptEngine>(
        [
            new FakeScriptEngine
            {
                ExecuteAsyncHandler = async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return ScriptExecutionResult.Succeeded();
                }
            },
            new FakeScriptEngine
            {
                ExecuteAsyncHandler = async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return ScriptExecutionResult.Succeeded();
                }
            }
        ]);
        var factory = new ScriptEngineFactory();
        factory.Register(ScriptLanguage.JavaScript, _ => engines.Dequeue());
        var manager = new ScriptTaskManager(factory);

        var first = manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });
        var second = manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });

        while (manager.GetTasks().Count < 2)
        {
            await Task.Yield();
        }

        Assert.Equal(2, manager.StopAll());

        Assert.Equal(ScriptTaskStatus.Canceled, (await first.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.Equal(ScriptTaskStatus.Canceled, (await second.WaitAsync(TimeSpan.FromSeconds(5))).Status);
    }

    [Fact]
    public void GetTasks_orders_by_descending_start_time()
    {
        var manager = new ScriptTaskManager(new ScriptEngineFactory());
        var first = manager.RegisterExternalTask("first");
        Thread.Sleep(20);
        var second = manager.RegisterExternalTask("second");

        var tasks = manager.GetTasks();

        Assert.Equal(second.Id, tasks[0].Id);
        Assert.Equal(first.Id, tasks[1].Id);
    }

    [Fact]
    public void ClearCompleted_removes_terminal_tasks_only()
    {
        var manager = new ScriptTaskManager(new ScriptEngineFactory());
        var running = manager.RegisterExternalTask("running");
        var completed = manager.RegisterExternalTask("completed");
        Assert.True(manager.CompleteExternalTask(completed.Id, ScriptTaskStatus.Completed));

        Assert.Equal(1, manager.ClearCompleted());

        Assert.NotNull(manager.GetTask(running.Id));
        Assert.Null(manager.GetTask(completed.Id));
    }

    [Fact]
    public void RegisterExternalTask_and_CompleteExternalTask_fire_lifecycle_events()
    {
        var manager = new ScriptTaskManager(new ScriptEngineFactory());
        ScriptTaskRecord? started = null;
        ScriptTaskRecord? completed = null;
        manager.TaskStarted += (_, task) => started = task;
        manager.TaskCompleted += (_, task) => completed = task;

        var external = manager.RegisterExternalTask(
            "host job",
            ScriptTaskOrigin.Host,
            ScriptLanguage.Janet,
            TimeSpan.FromMinutes(1),
            "job.janet");
        var completedResult = manager.CompleteExternalTask(
            external.Id,
            ScriptTaskStatus.Completed,
            ["done"]);

        Assert.True(completedResult);
        Assert.NotNull(started);
        Assert.NotNull(completed);
        Assert.Equal(external.Id, started.Id);
        Assert.Equal(ScriptTaskStatus.Running, started.Status);
        Assert.Equal(ScriptTaskStatus.Completed, completed.Status);
        Assert.Equal(["done"], completed.ReturnValues);
        Assert.Equal(ScriptLanguage.Janet, completed.Language);
        Assert.Equal("job.janet", completed.ScriptName);
    }

    [Fact]
    public async Task CompleteExternalTask_returns_false_for_missing_or_non_external_task()
    {
        var engine = new FakeScriptEngine();
        var manager = CreateManager(engine, ScriptLanguage.JavaScript);
        var runningTask = manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var running = Assert.Single(manager.GetTasks());

        Assert.False(manager.CompleteExternalTask("missing", ScriptTaskStatus.Completed));
        Assert.False(manager.CompleteExternalTask(running.Id, ScriptTaskStatus.Completed));

        engine.Complete(ScriptExecutionResult.Succeeded());
        await runningTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Task_snapshots_do_not_expose_internal_return_values()
    {
        var returnValues = new List<object?> { "original" };
        var manager = CreateManager(
            new FakeScriptEngine(new ScriptExecutionResult
            {
                Success = true,
                ReturnValues = returnValues
            }),
            ScriptLanguage.JavaScript);

        var final = await manager.StartAsync(new ScriptExecutionRequest { Language = ScriptLanguage.JavaScript });
        var finalArray = Assert.IsType<object?[]>(final.ReturnValues);
        finalArray[0] = "changed from snapshot";
        returnValues[0] = "changed from engine list";

        var lookup = manager.GetTask(final.Id);

        Assert.NotNull(lookup);
        Assert.Equal(["original"], lookup.ReturnValues);
    }

    private static ScriptTaskManager CreateManager(FakeScriptEngine engine, ScriptLanguage language)
    {
        var factory = new ScriptEngineFactory();
        factory.Register(language, _ => engine);
        return new ScriptTaskManager(factory);
    }

    private sealed class FakeScriptEngine : IScriptEngine
    {
        private readonly ScriptExecutionResult? _result;

        public FakeScriptEngine()
        {
        }

        public FakeScriptEngine(ScriptExecutionResult result)
        {
            _result = result;
        }

        public TaskCompletionSource<ScriptExecutionRequest> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ScriptExecutionResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<ScriptExecutionRequest, CancellationToken, Task<ScriptExecutionResult>>? ExecuteAsyncHandler { get; set; }

        public bool Disposed { get; private set; }

        public int StopCalls { get; private set; }

        public Task<ScriptExecutionResult> ExecuteAsync(
            ScriptExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(request);

            if (ExecuteAsyncHandler is not null)
            {
                return ExecuteAsyncHandler(request, cancellationToken);
            }

            return _result is not null ? Task.FromResult(_result) : Completion.Task;
        }

        public void Complete(ScriptExecutionResult result) => Completion.SetResult(result);

        public void Stop() => StopCalls++;

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeScriptLogger : IScriptLogger
    {
        public void Log(ScriptLogEntry entry)
        {
        }
    }

    private interface IFakeCapability : IScriptHostCapability
    {
    }

    private sealed class FakeCapability : IFakeCapability
    {
    }
}
