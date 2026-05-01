using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptWorkerTests
{
    [Fact]
    public void Worker_can_be_registered_and_retrieved_from_host_context()
    {
        var worker = new ScriptInMemoryWorker();
        var context = ScriptHostContext.Empty.WithCapability<IScriptWorker>(worker);

        Assert.True(context.TryGetCapability<IScriptWorker>(out var found));
        Assert.Same(worker, found);
        Assert.Same(worker, context.GetCapability<IScriptWorker>());
    }

    [Fact]
    public void Missing_worker_uses_capability_unavailable_reporting()
    {
        var unavailable = ScriptCapabilityUnavailable.For<IScriptWorker>();
        var result = unavailable.ToExecutionResult();

        Assert.False(ScriptHostContext.Empty.TryGetCapability<IScriptWorker>(out _));
        Assert.Equal(typeof(IScriptWorker).FullName, unavailable.CapabilityName);
        Assert.False(result.Success);
        Assert.Contains(nameof(IScriptWorker), result.Error);
    }

    [Fact]
    public async Task Worker_start_returns_pending_job_with_metadata()
    {
        var worker = new ScriptInMemoryWorker();
        var payload = new byte[] { 1, 2, 3 };

        var result = await worker.StartAsync(
            new ScriptWorkerRequest
            {
                WorkerName = "sync",
                Payload = payload,
                ContentType = "application/octet-stream",
                CorrelationId = "corr-1",
                SourceQueueName = "jobs",
                SourceQueueItemId = "queue-item-1"
            },
            new ScriptWorkerStartOptions
            {
                JobId = "job-1",
                Origin = ScriptTaskOrigin.Queue
            });

        Assert.True(result.Success);
        Assert.Equal("job-1", result.Value?.Id);
        Assert.Equal("sync", result.Value?.WorkerName);
        Assert.Equal(ScriptWorkerStatus.Pending, result.Value?.Status);
        Assert.Equal(payload, result.Value?.Payload.ToArray());
        Assert.Equal("application/octet-stream", result.Value?.ContentType);
        Assert.Equal("corr-1", result.Value?.CorrelationId);
        Assert.Equal("jobs", result.Value?.SourceQueueName);
        Assert.Equal("queue-item-1", result.Value?.SourceQueueItemId);
        Assert.Equal(ScriptTaskOrigin.Queue, result.Value?.Origin);
        Assert.Null(result.Value?.TaskId);
        Assert.Empty(result.Value!.ReturnValues);
    }

    [Fact]
    public async Task Worker_generates_job_ids_when_not_provided()
    {
        var worker = new ScriptInMemoryWorker();

        var first = await worker.StartAsync(new ScriptWorkerRequest { WorkerName = "sync" });
        var second = await worker.StartAsync(new ScriptWorkerRequest { WorkerName = "sync" });

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEmpty(first.Value!.Id);
        Assert.NotEmpty(second.Value!.Id);
        Assert.NotEqual(first.Value.Id, second.Value.Id);
        Assert.Equal(ScriptTaskOrigin.Worker, first.Value.Origin);
    }

    [Fact]
    public async Task Worker_defensively_copies_payloads()
    {
        var worker = new ScriptInMemoryWorker();
        var payload = new byte[] { 1, 2, 3 };

        await worker.StartAsync(
            new ScriptWorkerRequest
            {
                WorkerName = "sync",
                Payload = payload
            },
            new ScriptWorkerStartOptions { JobId = "job-1" });
        payload[0] = 9;

        var first = await worker.GetAsync("job-1");
        first.Value!.Payload.ToArray()[1] = 9;
        var second = await worker.GetAsync("job-1");

        Assert.Equal(new byte[] { 1, 2, 3 }, first.Value.Payload.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, second.Value?.Payload.ToArray());
    }

    [Fact]
    public async Task Worker_returns_already_exists_for_duplicate_job_ids()
    {
        var worker = new ScriptInMemoryWorker();

        await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "job-1" });
        var result = await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "job-1" });

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkerErrorCode.AlreadyExists, result.Error?.Code);
        Assert.Equal("sync", result.Error?.WorkerName);
        Assert.Equal("job-1", result.Error?.JobId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Worker_returns_invalid_worker_name_for_empty_names(string workerName)
    {
        var worker = new ScriptInMemoryWorker();

        var result = await worker.StartAsync(new ScriptWorkerRequest { WorkerName = workerName });

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkerErrorCode.InvalidWorkerName, result.Error?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Worker_returns_invalid_job_id_for_empty_ids(string jobId)
    {
        var worker = new ScriptInMemoryWorker();

        var start = await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = jobId });
        var get = await worker.GetAsync(jobId);
        var cancel = await worker.CancelAsync(jobId);
        var running = worker.MarkRunning(jobId);

        Assert.Equal(ScriptWorkerErrorCode.InvalidJobId, start.Error?.Code);
        Assert.Equal(ScriptWorkerErrorCode.InvalidJobId, get.Error?.Code);
        Assert.Equal(ScriptWorkerErrorCode.InvalidJobId, cancel.Error?.Code);
        Assert.Equal(ScriptWorkerErrorCode.InvalidJobId, running.Error?.Code);
    }

    [Fact]
    public async Task Worker_get_missing_job_returns_not_found()
    {
        var worker = new ScriptInMemoryWorker();

        var result = await worker.GetAsync("missing");

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkerErrorCode.NotFound, result.Error?.Code);
        Assert.Equal("missing", result.Error?.JobId);
    }

    [Fact]
    public async Task Worker_cancel_pending_job_transitions_to_canceled()
    {
        var worker = new ScriptInMemoryWorker();
        await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "job-1" });

        var result = await worker.CancelAsync("job-1");

        Assert.True(result.Success);
        Assert.Equal(ScriptWorkerStatus.Canceled, result.Value?.Status);
        Assert.NotNull(result.Value?.EndedAt);
        Assert.Equal("Worker job was canceled.", result.Value?.Error);
    }

    [Fact]
    public async Task Worker_cancel_running_job_transitions_to_canceled()
    {
        var worker = new ScriptInMemoryWorker();
        await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "job-1" });
        worker.MarkRunning("job-1", "task-1");

        var result = await worker.CancelAsync("job-1");

        Assert.True(result.Success);
        Assert.Equal(ScriptWorkerStatus.Canceled, result.Value?.Status);
        Assert.Equal("task-1", result.Value?.TaskId);
    }

    [Fact]
    public async Task Worker_cancel_completed_job_returns_invalid_state()
    {
        var worker = new ScriptInMemoryWorker();
        await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "job-1" });
        worker.MarkCompleted("job-1");

        var result = await worker.CancelAsync("job-1");

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkerErrorCode.InvalidState, result.Error?.Code);
    }

    [Fact]
    public async Task Worker_list_returns_jobs_in_created_descending_order()
    {
        var worker = new ScriptInMemoryWorker();
        await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "first" });
        await Task.Delay(1);
        await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "second" });

        var result = await worker.ListAsync();

        Assert.True(result.Success);
        Assert.Equal(["second", "first"], result.Value!.Select(job => job.Id).ToArray());
    }

    [Fact]
    public async Task Worker_helpers_mark_running_completed_failed_and_canceled_states()
    {
        var worker = new ScriptInMemoryWorker();
        await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "running" });
        await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "completed" });
        await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "failed" });
        await worker.StartAsync(
            new ScriptWorkerRequest { WorkerName = "sync" },
            new ScriptWorkerStartOptions { JobId = "canceled" });

        var running = worker.MarkRunning("running", "task-1");
        var completed = worker.MarkCompleted("completed", ["ok", 42]);
        var failed = worker.MarkFailed("failed", "Worker failed.");
        var canceled = worker.MarkCanceled("canceled", "Stopped.");

        Assert.Equal(ScriptWorkerStatus.Running, running.Value?.Status);
        Assert.Equal("task-1", running.Value?.TaskId);
        Assert.Equal(ScriptWorkerStatus.Completed, completed.Value?.Status);
        Assert.Equal(["ok", 42], completed.Value?.ReturnValues.ToArray());
        Assert.Equal(ScriptWorkerStatus.Failed, failed.Value?.Status);
        Assert.Equal("Worker failed.", failed.Value?.Error);
        Assert.Equal(ScriptWorkerStatus.Canceled, canceled.Value?.Status);
        Assert.Equal("Stopped.", canceled.Value?.Error);
    }

    [Fact]
    public async Task Worker_operations_observe_cancellation()
    {
        var worker = new ScriptInMemoryWorker();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            worker.StartAsync(
                new ScriptWorkerRequest { WorkerName = "sync" },
                cancellationToken: cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            worker.GetAsync("job-1", cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            worker.CancelAsync("job-1", cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            worker.ListAsync(cancellationTokenSource.Token));
    }

    [Fact]
    public void Worker_error_converts_to_failed_execution_result()
    {
        var error = new ScriptWorkerError(
            ScriptWorkerErrorCode.AccessDenied,
            "sync",
            "job-1",
            "Worker access was denied.");

        var result = error.ToExecutionResult();

        Assert.False(result.Success);
        Assert.Equal(error.Message, result.Error);
    }
}
