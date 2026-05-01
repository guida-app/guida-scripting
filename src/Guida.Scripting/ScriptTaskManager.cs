namespace Guida.Scripting;

/// <summary>
/// Tracks script task lifecycle records for host applications.
/// </summary>
public sealed class ScriptTaskManager
{
    private readonly object _syncRoot = new();
    private readonly ScriptEngineFactory _engineFactory;
    private readonly Dictionary<string, TaskState> _tasks = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new task manager.
    /// </summary>
    public ScriptTaskManager(ScriptEngineFactory engineFactory)
    {
        ArgumentNullException.ThrowIfNull(engineFactory);

        _engineFactory = engineFactory;
    }

    /// <summary>
    /// Fires after a task is registered as running. The event argument is a snapshot.
    /// </summary>
    public event EventHandler<ScriptTaskRecord>? TaskStarted;

    /// <summary>
    /// Fires after a task reaches a terminal status. The event argument is a snapshot.
    /// </summary>
    public event EventHandler<ScriptTaskRecord>? TaskCompleted;

    /// <summary>
    /// Starts a script task and returns its final task record.
    /// </summary>
    /// <remarks>
    /// The manager records and forwards timeout values to the selected engine. Timeout enforcement is owned by
    /// the engine; the manager maps <see cref="ScriptExecutionResult.IsTimedOut" /> to <see cref="ScriptTaskStatus.TimedOut" />.
    /// The engine created for the task is disposed before this method returns.
    /// </remarks>
    public async Task<ScriptTaskRecord> StartAsync(
        ScriptExecutionRequest request,
        ScriptTaskStartOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var language = ResolveLanguage(request, options);
        var timeout = options?.Timeout ?? request.Timeout;
        var taskName = GetTaskName(request, options);
        var taskId = CreateTaskId();
        var state = new TaskState(
            taskId,
            taskName,
            options?.Origin ?? ScriptTaskOrigin.User,
            language,
            DateTimeOffset.UtcNow,
            timeout,
            request.Name,
            isExternal: false);

        AddTask(state);
        TaskStarted?.Invoke(this, Snapshot(state));

        var hostContext = request.HostContext with
        {
            Execution = request.HostContext.Execution with
            {
                TaskId = taskId,
                Origin = state.Origin
            }
        };

        IScriptEngine? engine = null;
        ScriptExecutionResult? result = null;
        ScriptTaskStatus status = ScriptTaskStatus.Failed;
        string? error = null;
        IReadOnlyList<object?> returnValues = Array.Empty<object?>();
        var cancellationRequested = false;

        try
        {
            if (language == ScriptLanguage.Unknown)
            {
                throw new NotSupportedException("Could not determine the script language for this task.");
            }

            if (!_engineFactory.IsRegistered(language))
            {
                throw new NotSupportedException($"No script engine is registered for language '{language}'.");
            }

            state.CancellationTokenSource.Token.ThrowIfCancellationRequested();

            engine = _engineFactory.Create(new ScriptEngineCreationContext
            {
                Language = language,
                Name = request.Name,
                HostContext = hostContext
            });

            SetEngine(state, engine);

            result = await engine.ExecuteAsync(
                request with
                {
                    Language = language,
                    Timeout = timeout,
                    HostContext = hostContext
                },
                state.CancellationTokenSource.Token).ConfigureAwait(false);

            status = GetStatus(result);
            error = result.Error;
            returnValues = result.ReturnValues;
        }
        catch (OperationCanceledException exception)
        {
            status = ScriptTaskStatus.Canceled;
            error = string.IsNullOrWhiteSpace(exception.Message) ? "Script task was canceled." : exception.Message;
        }
        catch (Exception exception)
        {
            status = ScriptTaskStatus.Failed;
            error = exception.Message;
        }
        finally
        {
            cancellationRequested = state.CancellationTokenSource.IsCancellationRequested;

            if (engine is not null)
            {
                engine.Dispose();
            }

            state.CancellationTokenSource.Dispose();
        }

        if (result is { IsTimedOut: true })
        {
            status = ScriptTaskStatus.TimedOut;
        }
        else if (cancellationRequested)
        {
            status = ScriptTaskStatus.Canceled;
        }

        var completed = CompleteTask(state, status, returnValues, error);
        TaskCompleted?.Invoke(this, completed);

        return completed;
    }

    /// <summary>
    /// Requests cancellation for a running task.
    /// </summary>
    /// <returns><see langword="true" /> when the task was running and cancellation was requested; otherwise <see langword="false" />.</returns>
    public bool Stop(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        TaskState? state;
        IScriptEngine? engine;

        lock (_syncRoot)
        {
            if (!_tasks.TryGetValue(taskId, out state) || state.Status != ScriptTaskStatus.Running)
            {
                return false;
            }

            state.CancellationTokenSource.Cancel();
            engine = state.Engine;
        }

        engine?.Stop();
        return true;
    }

    /// <summary>
    /// Requests cancellation for all running tasks.
    /// </summary>
    /// <returns>The number of running tasks that accepted a stop request.</returns>
    public int StopAll()
    {
        TaskState[] runningTasks;

        lock (_syncRoot)
        {
            runningTasks = _tasks.Values
                .Where(task => task.Status == ScriptTaskStatus.Running)
                .ToArray();
        }

        var stopped = 0;
        foreach (var task in runningTasks)
        {
            if (Stop(task.Id))
            {
                stopped++;
            }
        }

        return stopped;
    }

    /// <summary>
    /// Gets a task snapshot by identifier.
    /// </summary>
    public ScriptTaskRecord? GetTask(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        lock (_syncRoot)
        {
            return _tasks.TryGetValue(taskId, out var state) ? Snapshot(state) : null;
        }
    }

    /// <summary>
    /// Gets task snapshots ordered by descending start time.
    /// </summary>
    public IReadOnlyList<ScriptTaskRecord> GetTasks()
    {
        lock (_syncRoot)
        {
            return _tasks.Values
                .OrderByDescending(task => task.StartedAt)
                .Select(Snapshot)
                .ToArray();
        }
    }

    /// <summary>
    /// Removes tasks that have reached a terminal status.
    /// </summary>
    /// <returns>The number of task records removed.</returns>
    public int ClearCompleted()
    {
        lock (_syncRoot)
        {
            var completedTaskIds = _tasks.Values
                .Where(task => task.Status != ScriptTaskStatus.Running)
                .Select(task => task.Id)
                .ToArray();

            foreach (var taskId in completedTaskIds)
            {
                _tasks.Remove(taskId);
            }

            return completedTaskIds.Length;
        }
    }

    /// <summary>
    /// Registers host-managed work as a running task and returns its running snapshot.
    /// </summary>
    public ScriptTaskRecord RegisterExternalTask(
        string name,
        ScriptTaskOrigin origin = ScriptTaskOrigin.External,
        ScriptLanguage language = ScriptLanguage.Unknown,
        TimeSpan? timeout = null,
        string? scriptName = null,
        string? taskId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        taskId ??= CreateTaskId();
        var state = new TaskState(
            taskId,
            name,
            origin,
            language,
            DateTimeOffset.UtcNow,
            timeout,
            scriptName,
            isExternal: true);

        AddTask(state);

        var snapshot = Snapshot(state);
        TaskStarted?.Invoke(this, snapshot);
        return snapshot;
    }

    /// <summary>
    /// Completes a host-managed task.
    /// </summary>
    /// <returns><see langword="true" /> when a running external task was completed; otherwise <see langword="false" />.</returns>
    public bool CompleteExternalTask(
        string taskId,
        ScriptTaskStatus status,
        IReadOnlyList<object?>? returnValues = null,
        string? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        if (status == ScriptTaskStatus.Running)
        {
            throw new ArgumentException("External tasks must complete with a terminal status.", nameof(status));
        }

        TaskState state;
        lock (_syncRoot)
        {
            if (!_tasks.TryGetValue(taskId, out state!) ||
                !state.IsExternal ||
                state.Status != ScriptTaskStatus.Running)
            {
                return false;
            }
        }

        var completed = CompleteTask(state, status, returnValues ?? Array.Empty<object?>(), error);
        TaskCompleted?.Invoke(this, completed);
        return true;
    }

    private static string CreateTaskId() => Guid.NewGuid().ToString("N");

    private static ScriptTaskStatus GetStatus(ScriptExecutionResult result)
    {
        if (result.IsTimedOut)
        {
            return ScriptTaskStatus.TimedOut;
        }

        if (result.IsCanceled)
        {
            return ScriptTaskStatus.Canceled;
        }

        return result.Success ? ScriptTaskStatus.Completed : ScriptTaskStatus.Failed;
    }

    private static string GetTaskName(ScriptExecutionRequest request, ScriptTaskStartOptions? options) =>
        !string.IsNullOrWhiteSpace(options?.Name)
            ? options.Name
            : !string.IsNullOrWhiteSpace(request.Name)
                ? request.Name
                : "Script task";

    private static ScriptLanguage ResolveLanguage(
        ScriptExecutionRequest request,
        ScriptTaskStartOptions? options)
    {
        if (options?.Language is { } optionLanguage)
        {
            return optionLanguage;
        }

        if (request.Language != ScriptLanguage.Unknown)
        {
            return request.Language;
        }

        var detected = ScriptLanguageDetector.Detect(request.Name);
        return detected != ScriptLanguage.Unknown ? detected : ScriptLanguageDetector.Detect(options?.Name);
    }

    private void AddTask(TaskState state)
    {
        lock (_syncRoot)
        {
            if (_tasks.ContainsKey(state.Id))
            {
                throw new ArgumentException($"A task with id '{state.Id}' is already registered.", nameof(state));
            }

            _tasks.Add(state.Id, state);
        }
    }

    private void SetEngine(TaskState state, IScriptEngine engine)
    {
        lock (_syncRoot)
        {
            state.Engine = engine;
        }
    }

    private ScriptTaskRecord CompleteTask(
        TaskState state,
        ScriptTaskStatus status,
        IReadOnlyList<object?> returnValues,
        string? error)
    {
        lock (_syncRoot)
        {
            state.Status = status;
            state.EndedAt = DateTimeOffset.UtcNow;
            state.Duration = state.EndedAt - state.StartedAt;
            state.ReturnValues = returnValues.ToArray();
            state.Error = error;
            state.Engine = null;

            return Snapshot(state);
        }
    }

    private static ScriptTaskRecord Snapshot(TaskState state) =>
        new()
        {
            Id = state.Id,
            Name = state.Name,
            Origin = state.Origin,
            Language = state.Language,
            Status = state.Status,
            StartedAt = state.StartedAt,
            EndedAt = state.EndedAt,
            Duration = state.Duration,
            Error = state.Error,
            ReturnValues = state.ReturnValues.ToArray(),
            Timeout = state.Timeout,
            ScriptName = state.ScriptName
        };

    private sealed class TaskState
    {
        public TaskState(
            string id,
            string name,
            ScriptTaskOrigin origin,
            ScriptLanguage language,
            DateTimeOffset startedAt,
            TimeSpan? timeout,
            string? scriptName,
            bool isExternal)
        {
            Id = id;
            Name = name;
            Origin = origin;
            Language = language;
            StartedAt = startedAt;
            Timeout = timeout;
            ScriptName = scriptName;
            IsExternal = isExternal;
        }

        public string Id { get; }

        public string Name { get; }

        public ScriptTaskOrigin Origin { get; }

        public ScriptLanguage Language { get; }

        public DateTimeOffset StartedAt { get; }

        public TimeSpan? Timeout { get; }

        public string? ScriptName { get; }

        public bool IsExternal { get; }

        public ScriptTaskStatus Status { get; set; } = ScriptTaskStatus.Running;

        public DateTimeOffset? EndedAt { get; set; }

        public TimeSpan? Duration { get; set; }

        public string? Error { get; set; }

        public IReadOnlyList<object?> ReturnValues { get; set; } = Array.Empty<object?>();

        public CancellationTokenSource CancellationTokenSource { get; } = new();

        public IScriptEngine? Engine { get; set; }
    }
}
