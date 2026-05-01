using System.Collections.ObjectModel;

namespace Guida.Scripting;

/// <summary>
/// In-memory worker capability for tests, samples, and simple hosts.
/// </summary>
public sealed class ScriptInMemoryWorker : IScriptWorker
{
    private readonly Dictionary<string, WorkerJobState> _jobs = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _nextId;

    /// <inheritdoc />
    public Task<ScriptWorkerResult<ScriptWorkerJob>> StartAsync(
        ScriptWorkerRequest request,
        ScriptWorkerStartOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workerValidation = ValidateWorkerName(request?.WorkerName);
        if (!workerValidation.Success)
        {
            return Task.FromResult(ScriptWorkerResult<ScriptWorkerJob>.Failed(workerValidation.Error!));
        }

        options ??= new ScriptWorkerStartOptions();
        var jobId = options.JobId ?? CreateJobId();
        var jobValidation = ValidateJobId(jobId);
        if (!jobValidation.Success)
        {
            return Task.FromResult(ScriptWorkerResult<ScriptWorkerJob>.Failed(jobValidation.Error!));
        }

        lock (_gate)
        {
            if (_jobs.ContainsKey(jobId))
            {
                return Task.FromResult(ScriptWorkerResult<ScriptWorkerJob>.Failed(
                    Failed(
                        ScriptWorkerErrorCode.AlreadyExists,
                        request!.WorkerName,
                        jobId,
                        $"Worker job '{jobId}' already exists.")));
            }

            var now = DateTimeOffset.UtcNow;
            var state = new WorkerJobState(
                jobId,
                request!.WorkerName,
                ScriptWorkerStatus.Pending,
                Copy(request.Payload),
                request.ContentType,
                request.CorrelationId,
                request.SourceQueueName,
                request.SourceQueueItemId,
                options.Origin,
                now,
                startedAt: null,
                endedAt: null,
                taskId: null,
                error: null,
                returnValues: Array.Empty<object?>());

            _jobs[jobId] = state;

            return Task.FromResult(ScriptWorkerResult<ScriptWorkerJob>.Succeeded(ToJob(state)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkerResult<ScriptWorkerJob>> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateJobId(jobId);
        if (!validation.Success)
        {
            return Task.FromResult(ScriptWorkerResult<ScriptWorkerJob>.Failed(validation.Error!));
        }

        lock (_gate)
        {
            var state = FindState(jobId);
            if (state is null)
            {
                return Task.FromResult(ScriptWorkerResult<ScriptWorkerJob>.Failed(NotFound(jobId)));
            }

            return Task.FromResult(ScriptWorkerResult<ScriptWorkerJob>.Succeeded(ToJob(state)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkerResult<ScriptWorkerJob>> CancelAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(MarkCanceled(jobId, "Worker job was canceled."));
    }

    /// <inheritdoc />
    public Task<ScriptWorkerResult<IReadOnlyList<ScriptWorkerJob>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var jobs = _jobs.Values
                .OrderByDescending(job => job.CreatedAt)
                .Select(ToJob)
                .ToArray();

            return Task.FromResult(ScriptWorkerResult<IReadOnlyList<ScriptWorkerJob>>.Succeeded(
                new ReadOnlyCollection<ScriptWorkerJob>(jobs)));
        }
    }

    /// <summary>
    /// Marks a job as running.
    /// </summary>
    public ScriptWorkerResult<ScriptWorkerJob> MarkRunning(string jobId, string? taskId = null)
    {
        var validation = ValidateJobId(jobId);
        if (!validation.Success)
        {
            return ScriptWorkerResult<ScriptWorkerJob>.Failed(validation.Error!);
        }

        lock (_gate)
        {
            var state = FindState(jobId);
            if (state is null)
            {
                return ScriptWorkerResult<ScriptWorkerJob>.Failed(NotFound(jobId));
            }

            if (IsTerminal(state.Status))
            {
                return ScriptWorkerResult<ScriptWorkerJob>.Failed(InvalidState(state));
            }

            state.Status = ScriptWorkerStatus.Running;
            state.StartedAt ??= DateTimeOffset.UtcNow;
            state.TaskId = taskId ?? state.TaskId;

            return ScriptWorkerResult<ScriptWorkerJob>.Succeeded(ToJob(state));
        }
    }

    /// <summary>
    /// Marks a job as completed.
    /// </summary>
    public ScriptWorkerResult<ScriptWorkerJob> MarkCompleted(
        string jobId,
        IReadOnlyList<object?>? returnValues = null)
    {
        var validation = ValidateJobId(jobId);
        if (!validation.Success)
        {
            return ScriptWorkerResult<ScriptWorkerJob>.Failed(validation.Error!);
        }

        lock (_gate)
        {
            var state = FindState(jobId);
            if (state is null)
            {
                return ScriptWorkerResult<ScriptWorkerJob>.Failed(NotFound(jobId));
            }

            if (IsTerminal(state.Status))
            {
                return ScriptWorkerResult<ScriptWorkerJob>.Failed(InvalidState(state));
            }

            state.Status = ScriptWorkerStatus.Completed;
            state.StartedAt ??= DateTimeOffset.UtcNow;
            state.EndedAt = DateTimeOffset.UtcNow;
            state.Error = null;
            state.ReturnValues = returnValues?.ToArray() ?? Array.Empty<object?>();

            return ScriptWorkerResult<ScriptWorkerJob>.Succeeded(ToJob(state));
        }
    }

    /// <summary>
    /// Marks a job as failed.
    /// </summary>
    public ScriptWorkerResult<ScriptWorkerJob> MarkFailed(string jobId, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        var validation = ValidateJobId(jobId);
        if (!validation.Success)
        {
            return ScriptWorkerResult<ScriptWorkerJob>.Failed(validation.Error!);
        }

        lock (_gate)
        {
            var state = FindState(jobId);
            if (state is null)
            {
                return ScriptWorkerResult<ScriptWorkerJob>.Failed(NotFound(jobId));
            }

            if (IsTerminal(state.Status))
            {
                return ScriptWorkerResult<ScriptWorkerJob>.Failed(InvalidState(state));
            }

            state.Status = ScriptWorkerStatus.Failed;
            state.StartedAt ??= DateTimeOffset.UtcNow;
            state.EndedAt = DateTimeOffset.UtcNow;
            state.Error = error;
            state.ReturnValues = Array.Empty<object?>();

            return ScriptWorkerResult<ScriptWorkerJob>.Succeeded(ToJob(state));
        }
    }

    /// <summary>
    /// Marks a job as canceled.
    /// </summary>
    public ScriptWorkerResult<ScriptWorkerJob> MarkCanceled(string jobId, string? error = null)
    {
        var validation = ValidateJobId(jobId);
        if (!validation.Success)
        {
            return ScriptWorkerResult<ScriptWorkerJob>.Failed(validation.Error!);
        }

        lock (_gate)
        {
            var state = FindState(jobId);
            if (state is null)
            {
                return ScriptWorkerResult<ScriptWorkerJob>.Failed(NotFound(jobId));
            }

            if (IsTerminal(state.Status))
            {
                return ScriptWorkerResult<ScriptWorkerJob>.Failed(InvalidState(state));
            }

            state.Status = ScriptWorkerStatus.Canceled;
            state.EndedAt = DateTimeOffset.UtcNow;
            state.Error = error;
            state.ReturnValues = Array.Empty<object?>();

            return ScriptWorkerResult<ScriptWorkerJob>.Succeeded(ToJob(state));
        }
    }

    private static bool IsTerminal(ScriptWorkerStatus status) =>
        status is ScriptWorkerStatus.Completed or ScriptWorkerStatus.Failed or ScriptWorkerStatus.Canceled;

    private WorkerJobState? FindState(string jobId) =>
        _jobs.TryGetValue(jobId, out var state) ? state : null;

    private string CreateJobId() => Interlocked.Increment(ref _nextId).ToString("D", null);

    private static ScriptWorkerResult ValidateWorkerName(string? workerName)
    {
        if (string.IsNullOrWhiteSpace(workerName))
        {
            return ScriptWorkerResult.Failed(
                Failed(
                    ScriptWorkerErrorCode.InvalidWorkerName,
                    workerName ?? string.Empty,
                    string.Empty,
                    "Worker name cannot be empty."));
        }

        return ScriptWorkerResult.Succeeded();
    }

    private static ScriptWorkerResult ValidateJobId(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return ScriptWorkerResult.Failed(
                Failed(
                    ScriptWorkerErrorCode.InvalidJobId,
                    string.Empty,
                    jobId ?? string.Empty,
                    "Worker job id cannot be empty."));
        }

        return ScriptWorkerResult.Succeeded();
    }

    private static ScriptWorkerError NotFound(string jobId) =>
        Failed(
            ScriptWorkerErrorCode.NotFound,
            string.Empty,
            jobId,
            $"Worker job '{jobId}' was not found.");

    private static ScriptWorkerError InvalidState(WorkerJobState state) =>
        Failed(
            ScriptWorkerErrorCode.InvalidState,
            state.WorkerName,
            state.Id,
            $"Worker job '{state.Id}' cannot transition from '{state.Status}'.");

    private static ScriptWorkerError Failed(
        ScriptWorkerErrorCode code,
        string workerName,
        string jobId,
        string message) =>
        new(code, workerName, jobId, message);

    private static ScriptWorkerJob ToJob(WorkerJobState state) =>
        new()
        {
            Id = state.Id,
            WorkerName = state.WorkerName,
            Status = state.Status,
            Payload = Copy(state.Payload),
            ContentType = state.ContentType,
            CorrelationId = state.CorrelationId,
            SourceQueueName = state.SourceQueueName,
            SourceQueueItemId = state.SourceQueueItemId,
            Origin = state.Origin,
            CreatedAt = state.CreatedAt,
            StartedAt = state.StartedAt,
            EndedAt = state.EndedAt,
            TaskId = state.TaskId,
            Error = state.Error,
            ReturnValues = state.ReturnValues.ToArray()
        };

    private static byte[] Copy(ReadOnlyMemory<byte> content) => content.ToArray();

    private sealed class WorkerJobState
    {
        public WorkerJobState(
            string id,
            string workerName,
            ScriptWorkerStatus status,
            byte[] payload,
            string? contentType,
            string? correlationId,
            string? sourceQueueName,
            string? sourceQueueItemId,
            ScriptTaskOrigin origin,
            DateTimeOffset createdAt,
            DateTimeOffset? startedAt,
            DateTimeOffset? endedAt,
            string? taskId,
            string? error,
            IReadOnlyList<object?> returnValues)
        {
            Id = id;
            WorkerName = workerName;
            Status = status;
            Payload = payload;
            ContentType = contentType;
            CorrelationId = correlationId;
            SourceQueueName = sourceQueueName;
            SourceQueueItemId = sourceQueueItemId;
            Origin = origin;
            CreatedAt = createdAt;
            StartedAt = startedAt;
            EndedAt = endedAt;
            TaskId = taskId;
            Error = error;
            ReturnValues = returnValues;
        }

        public string Id { get; }

        public string WorkerName { get; }

        public ScriptWorkerStatus Status { get; set; }

        public byte[] Payload { get; }

        public string? ContentType { get; }

        public string? CorrelationId { get; }

        public string? SourceQueueName { get; }

        public string? SourceQueueItemId { get; }

        public ScriptTaskOrigin Origin { get; }

        public DateTimeOffset CreatedAt { get; }

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? EndedAt { get; set; }

        public string? TaskId { get; set; }

        public string? Error { get; set; }

        public IReadOnlyList<object?> ReturnValues { get; set; }
    }
}
