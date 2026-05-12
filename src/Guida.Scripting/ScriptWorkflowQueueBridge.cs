namespace Guida.Scripting;

/// <summary>
/// Helper methods that connect workflow ledger items to host-owned queues.
/// </summary>
public static class ScriptWorkflowQueueBridge
{
    /// <summary>
    /// Content type used for workflow queue envelope payloads.
    /// </summary>
    public const string EnvelopeContentType = "application/vnd.guida.workflow-queue-envelope+json";

    /// <summary>
    /// Upserts a workflow ledger item, enqueues a workflow envelope, and appends an idempotent enqueue event.
    /// </summary>
    public static async Task<ScriptWorkflowBridgeResult<ScriptWorkflowQueueEnqueueResult>> EnqueueAsync(
        IScriptWorkflowLedger ledger,
        IScriptQueue queue,
        ScriptWorkflowQueueEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(queue);
        cancellationToken.ThrowIfCancellationRequested();

        if (request == null)
        {
            return Failed<ScriptWorkflowQueueEnqueueResult>(
                ScriptWorkflowBridgeErrorCode.InvalidRequest,
                "Workflow queue enqueue request is required.");
        }

        var queueName = TrimToNull(request.QueueName);
        if (queueName == null)
        {
            return Failed<ScriptWorkflowQueueEnqueueResult>(
                ScriptWorkflowBridgeErrorCode.InvalidRequest,
                "Queue name cannot be empty.");
        }

        var item = await ledger.UpsertItemAsync(
            new ScriptWorkflowItemUpsert
            {
                WorkflowName = request.WorkflowName,
                ItemKey = request.ItemKey,
                ItemType = request.ItemType,
                RunId = request.RunId,
                Stage = request.Stage,
                State = request.State,
                Priority = request.Priority,
                MaxAttempts = request.MaxAttempts,
                NextRetryAt = request.NextRetryAt,
                MetadataJson = request.MetadataJson
            },
            cancellationToken).ConfigureAwait(false);
        if (!item.Success)
        {
            return Failed<ScriptWorkflowQueueEnqueueResult>(item.Error!);
        }

        var envelope = CreateEnvelope(item.Value!, request);
        var queueItemId = TrimToNull(request.QueueItemId) ?? item.Value!.Id;
        var enqueue = await queue.EnqueueAsync(
            queueName,
            envelope.ToJsonBytes(),
            new ScriptQueueEnqueueOptions
            {
                ItemId = queueItemId,
                ContentType = EnvelopeContentType,
                AvailableAt = request.AvailableAt,
                Delay = request.Delay
            },
            cancellationToken).ConfigureAwait(false);

        var queueItemAlreadyExisted = false;
        ScriptQueueItem queueItem;
        if (enqueue.Success)
        {
            queueItem = enqueue.Value!;
        }
        else if (enqueue.Error?.Code == ScriptQueueErrorCode.AlreadyExists)
        {
            var existing = await queue.GetAsync(queueName, queueItemId, cancellationToken).ConfigureAwait(false);
            if (!existing.Success)
            {
                return Failed<ScriptWorkflowQueueEnqueueResult>(existing.Error!);
            }

            queueItem = existing.Value!;
            queueItemAlreadyExisted = true;
        }
        else
        {
            return Failed<ScriptWorkflowQueueEnqueueResult>(enqueue.Error!);
        }

        var eventType = TrimToNull(request.EventType) ?? "enqueued";
        var idempotencyKey = TrimToNull(request.EventIdempotencyKey) ??
            CreateEnqueueEventIdempotencyKey(queueName, queueItem.Id);
        var append = await ledger.AppendEventAsync(
            item.Value!.Id,
            new ScriptWorkflowEventAppend
            {
                RunId = item.Value.RunId,
                EventType = eventType,
                Stage = item.Value.Stage,
                State = item.Value.State,
                Message = $"Enqueued workflow item '{item.Value.ItemKey}' to queue '{queueName}'.",
                IdempotencyKey = idempotencyKey,
                MetadataJson = request.EventMetadataJson ?? request.MetadataJson
            },
            cancellationToken).ConfigureAwait(false);
        if (!append.Success)
        {
            return Failed<ScriptWorkflowQueueEnqueueResult>(append.Error!);
        }

        return ScriptWorkflowBridgeResult<ScriptWorkflowQueueEnqueueResult>.Succeeded(
            new ScriptWorkflowQueueEnqueueResult(
                item.Value,
                queueItem,
                append.Value!,
                envelope,
                queueItemAlreadyExisted));
    }

    /// <summary>
    /// Creates the default idempotency key for a workflow enqueue event.
    /// </summary>
    public static string CreateEnqueueEventIdempotencyKey(string queueName, string queueItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueItemId);
        return "workflow.enqueue:" + queueName.Trim() + ":" + queueItemId.Trim();
    }

    private static ScriptWorkflowQueueEnvelope CreateEnvelope(
        ScriptWorkflowItem item,
        ScriptWorkflowQueueEnqueueRequest request) =>
        new()
        {
            WorkflowName = item.WorkflowName,
            ItemId = item.Id,
            ItemKey = item.ItemKey,
            ItemType = item.ItemType,
            RunId = item.RunId,
            Stage = item.Stage,
            State = item.State,
            Priority = item.Priority,
            AttemptCount = item.AttemptCount,
            MaxAttempts = item.MaxAttempts,
            PayloadContentType = request.PayloadContentType,
            Payload = request.Payload.ToArray(),
            MetadataJson = request.MetadataJson
        };

    private static ScriptWorkflowBridgeResult<T> Failed<T>(ScriptWorkflowLedgerError error) =>
        ScriptWorkflowBridgeResult<T>.Failed(new ScriptWorkflowBridgeError(
            ScriptWorkflowBridgeErrorCode.WorkflowLedgerError,
            error.Message,
            ledgerError: error));

    private static ScriptWorkflowBridgeResult<T> Failed<T>(ScriptQueueError error) =>
        ScriptWorkflowBridgeResult<T>.Failed(new ScriptWorkflowBridgeError(
            ScriptWorkflowBridgeErrorCode.QueueError,
            error.Message,
            queueError: error));

    private static ScriptWorkflowBridgeResult<T> Failed<T>(
        ScriptWorkflowBridgeErrorCode code,
        string message) =>
        ScriptWorkflowBridgeResult<T>.Failed(new ScriptWorkflowBridgeError(code, message));

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
