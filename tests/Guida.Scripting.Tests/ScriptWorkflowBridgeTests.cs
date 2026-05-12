using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptWorkflowBridgeTests
{
    [Fact]
    public async Task Enqueue_upserts_workflow_item_queues_envelope_and_records_idempotent_event()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var queue = new ScriptInMemoryQueue();
        var run = await ledger.StartRunAsync("crawl");
        var request = new ScriptWorkflowQueueEnqueueRequest
        {
            WorkflowName = "crawl",
            ItemKey = "url-1",
            ItemType = "page",
            RunId = run.Value!.Id,
            Stage = "fetch",
            QueueName = "workflow",
            Payload = "https://example.com"u8.ToArray(),
            PayloadContentType = "text/plain",
            Priority = 9,
            MaxAttempts = 3,
            MetadataJson = """{"source":"seed"}"""
        };

        var first = await ScriptWorkflowQueueBridge.EnqueueAsync(ledger, queue, request);
        var second = await ScriptWorkflowQueueBridge.EnqueueAsync(ledger, queue, request);
        var queued = await queue.ListAsync("workflow");
        var events = await ledger.GetEventsForItemAsync(first.Value!.Item.Id);
        var envelope = ScriptWorkflowQueueEnvelope.FromJson(first.Value.QueueItem.Payload);

        Assert.True(first.Success, first.Error?.Message);
        Assert.True(second.Success, second.Error?.Message);
        Assert.False(first.Value.QueueItemAlreadyExisted);
        Assert.True(second.Value!.QueueItemAlreadyExisted);
        Assert.Equal(first.Value.Item.Id, second.Value.Item.Id);
        Assert.Equal(first.Value.QueueItem.Id, second.Value.QueueItem.Id);
        Assert.Equal(first.Value.Event.Id, second.Value.Event.Id);
        Assert.Single(queued.Value!);
        Assert.Single(events.Value!);
        Assert.Equal(ScriptWorkflowQueueBridge.EnvelopeContentType, first.Value.QueueItem.ContentType);
        Assert.Equal(ScriptWorkflowQueueBridge.CreateEnqueueEventIdempotencyKey("workflow", first.Value.QueueItem.Id), first.Value.Event.IdempotencyKey);
        Assert.True(envelope.Success, envelope.Error?.Message);
        Assert.Equal("crawl", envelope.Value!.WorkflowName);
        Assert.Equal("url-1", envelope.Value.ItemKey);
        Assert.Equal(first.Value.Item.Id, envelope.Value.ItemId);
        Assert.Equal("fetch", envelope.Value.Stage);
        Assert.Equal("pending", envelope.Value.State);
        Assert.Equal(9, envelope.Value.Priority);
        Assert.Equal(3, envelope.Value.MaxAttempts);
        Assert.Equal("text/plain", envelope.Value.PayloadContentType);
        Assert.Equal("https://example.com"u8.ToArray(), envelope.Value.Payload);
        Assert.Equal("""{"source":"seed"}""", envelope.Value.MetadataJson);
    }

    [Fact]
    public async Task Enqueue_reports_queue_failures_without_appending_enqueue_event()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var queue = new ScriptInMemoryQueue();

        var result = await ScriptWorkflowQueueBridge.EnqueueAsync(
            ledger,
            queue,
            new ScriptWorkflowQueueEnqueueRequest
            {
                WorkflowName = "crawl",
                ItemKey = "url-1",
                Stage = "fetch",
                QueueName = " "
            });

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkflowBridgeErrorCode.InvalidRequest, result.Error?.Code);

        var items = await ledger.QueryItemsAsync(new ScriptWorkflowItemQuery { WorkflowName = "crawl" });
        Assert.Empty(items.Value!);
    }

    [Fact]
    public async Task Worker_bridge_dispatches_queue_item_and_uses_task_id_for_workflow_lease()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var queue = new ScriptInMemoryQueue();
        var worker = new ScriptInMemoryWorker();
        var enqueued = await ScriptWorkflowQueueBridge.EnqueueAsync(
            ledger,
            queue,
            new ScriptWorkflowQueueEnqueueRequest
            {
                WorkflowName = "crawl",
                ItemKey = "url-1",
                Stage = "fetch",
                QueueName = "workflow",
                Payload = """{"url":"https://example.com"}"""u8.ToArray(),
                PayloadContentType = "application/json"
            });
        var claimedQueueItem = Assert.Single((await queue.ClaimAsync("workflow")).Value!);
        var envelope = ScriptWorkflowQueueEnvelope.FromJson(claimedQueueItem.Payload).Value!;

        var job = await ScriptWorkflowWorkerBridge.StartWorkerAsync(
            worker,
            claimedQueueItem,
            envelope,
            "fetcher",
            new ScriptWorkflowWorkerDispatchOptions { JobId = "job-1" });
        var running = worker.MarkRunning(job.Value!.Id, "task-123");
        var context = ScriptWorkflowWorkerBridge.CreateContext(running.Value!, envelope);
        var claimedWorkflowItem = await ScriptWorkflowWorkerBridge.ClaimItemAsync(
            ledger,
            context.Value!,
            TimeSpan.FromMinutes(5));
        var completed = await ScriptWorkflowWorkerBridge.CompleteItemAsync(
            ledger,
            context.Value!,
            """{"ok":true}""");

        Assert.True(job.Success, job.Error?.Message);
        Assert.Equal("workflow", job.Value.SourceQueueName);
        Assert.Equal(claimedQueueItem.Id, job.Value.SourceQueueItemId);
        Assert.Equal(enqueued.Value!.Item.Id, job.Value.CorrelationId);
        Assert.Equal(ScriptTaskOrigin.Queue, job.Value.Origin);
        Assert.True(context.Success, context.Error?.Message);
        Assert.Equal("task-123", context.Value!.LeaseOwner);
        Assert.Equal(enqueued.Value.Item.Id, context.Value.ItemId);
        Assert.Equal("task-123", claimedWorkflowItem.Value!.LeaseOwner);
        Assert.Equal("completed", completed.Value!.State);
        Assert.Null(completed.Value.LeaseOwner);
        Assert.Equal("""{"ok":true}""", completed.Value.MetadataJson);
    }

    [Fact]
    public async Task Worker_bridge_falls_back_to_job_id_when_task_id_is_not_available()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var queue = new ScriptInMemoryQueue();
        var worker = new ScriptInMemoryWorker();
        var enqueued = await ScriptWorkflowQueueBridge.EnqueueAsync(
            ledger,
            queue,
            new ScriptWorkflowQueueEnqueueRequest
            {
                WorkflowName = "crawl",
                ItemKey = "url-1",
                Stage = "fetch",
                QueueName = "workflow"
            });
        var queueItem = Assert.Single((await queue.ClaimAsync("workflow")).Value!);
        var envelope = enqueued.Value!.Envelope;
        var job = await ScriptWorkflowWorkerBridge.StartWorkerAsync(
            worker,
            queueItem,
            envelope,
            "fetcher",
            new ScriptWorkflowWorkerDispatchOptions { JobId = "job-1" });

        var context = ScriptWorkflowWorkerBridge.CreateContext(job.Value!, envelope);
        var claimed = await ScriptWorkflowWorkerBridge.ClaimItemAsync(
            ledger,
            context.Value!,
            TimeSpan.FromMinutes(5));
        var failed = await ScriptWorkflowWorkerBridge.FailItemAsync(
            ledger,
            context.Value!,
            "temporary",
            "Transient");

        Assert.Equal("job-1", context.Value!.LeaseOwner);
        Assert.Equal("job-1", claimed.Value!.LeaseOwner);
        Assert.Equal("retry_ready", failed.Value!.State);
        Assert.Equal("temporary", failed.Value.LastError);
        Assert.Equal("Transient", failed.Value.LastErrorType);
    }

    [Fact]
    public async Task Bridge_methods_observe_cancellation_tokens()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var queue = new ScriptInMemoryQueue();
        var worker = new ScriptInMemoryWorker();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ScriptWorkflowQueueBridge.EnqueueAsync(
                ledger,
                queue,
                new ScriptWorkflowQueueEnqueueRequest(),
                cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ScriptWorkflowWorkerBridge.StartWorkerAsync(
                worker,
                new ScriptQueueItem(),
                new ScriptWorkflowQueueEnvelope(),
                "worker",
                cancellationToken: cts.Token));
    }

    [Fact]
    public void Envelope_rejects_invalid_json()
    {
        var result = ScriptWorkflowQueueEnvelope.FromJson("{ nope"u8.ToArray());

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkflowBridgeErrorCode.InvalidEnvelope, result.Error?.Code);
    }
}
