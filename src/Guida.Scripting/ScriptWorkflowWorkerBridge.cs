namespace Guida.Scripting;

/// <summary>
/// Helper methods that connect host-owned worker jobs to workflow ledger items.
/// </summary>
public static class ScriptWorkflowWorkerBridge
{
    /// <summary>
    /// Starts a worker job for a claimed queue item carrying a workflow queue envelope.
    /// </summary>
    public static async Task<ScriptWorkflowBridgeResult<ScriptWorkerJob>> StartWorkerAsync(
        IScriptWorker worker,
        ScriptQueueItem queueItem,
        ScriptWorkflowQueueEnvelope envelope,
        string workerName,
        ScriptWorkflowWorkerDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(queueItem);
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        options ??= new ScriptWorkflowWorkerDispatchOptions();
        var result = await worker.StartAsync(
            new ScriptWorkerRequest
            {
                WorkerName = workerName,
                Payload = queueItem.Payload,
                ContentType = queueItem.ContentType,
                CorrelationId = options.CorrelationId ?? envelope.ItemId,
                SourceQueueName = queueItem.QueueName,
                SourceQueueItemId = queueItem.Id
            },
            new ScriptWorkerStartOptions
            {
                JobId = options.JobId,
                Origin = options.Origin
            },
            cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ScriptWorkflowBridgeResult<ScriptWorkerJob>.Succeeded(result.Value!)
            : Failed<ScriptWorkerJob>(result.Error!);
    }

    /// <summary>
    /// Creates workflow context for a worker job and queue envelope.
    /// </summary>
    public static ScriptWorkflowBridgeResult<ScriptWorkflowWorkerContext> CreateContext(
        ScriptWorkerJob job,
        ScriptWorkflowQueueEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(envelope);

        if (string.IsNullOrWhiteSpace(job.Id))
        {
            return InvalidContext("Worker job id cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(envelope.ItemId) ||
            string.IsNullOrWhiteSpace(envelope.WorkflowName) ||
            string.IsNullOrWhiteSpace(envelope.ItemKey))
        {
            return InvalidContext("Workflow queue envelope is missing required item fields.");
        }

        return ScriptWorkflowBridgeResult<ScriptWorkflowWorkerContext>.Succeeded(
            new ScriptWorkflowWorkerContext
            {
                WorkerName = job.WorkerName,
                JobId = job.Id,
                TaskId = TrimToNull(job.TaskId),
                SourceQueueName = job.SourceQueueName,
                SourceQueueItemId = job.SourceQueueItemId,
                WorkflowName = envelope.WorkflowName,
                RunId = envelope.RunId,
                ItemId = envelope.ItemId,
                ItemKey = envelope.ItemKey,
                Stage = envelope.Stage,
                State = envelope.State
            });
    }

    /// <summary>
    /// Claims the workflow item for a worker context using the task id as lease owner when available.
    /// </summary>
    public static async Task<ScriptWorkflowBridgeResult<ScriptWorkflowItem>> ClaimItemAsync(
        IScriptWorkflowLedger ledger,
        ScriptWorkflowWorkerContext context,
        TimeSpan leaseDuration,
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await ledger.ClaimItemAsync(
            context.ItemId,
            new ScriptWorkflowClaimOptions
            {
                LeaseOwner = context.LeaseOwner,
                LeaseDuration = leaseDuration,
                NowUtc = nowUtc
            },
            cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ScriptWorkflowBridgeResult<ScriptWorkflowItem>.Succeeded(result.Value!)
            : Failed<ScriptWorkflowItem>(result.Error!);
    }

    /// <summary>
    /// Completes the workflow item for a worker context using the context lease owner.
    /// </summary>
    public static async Task<ScriptWorkflowBridgeResult<ScriptWorkflowItem>> CompleteItemAsync(
        IScriptWorkflowLedger ledger,
        ScriptWorkflowWorkerContext context,
        string? metadataJson = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await ledger.CompleteItemAsync(
            context.ItemId,
            new ScriptWorkflowItemCompleteOptions
            {
                LeaseOwner = context.LeaseOwner,
                MetadataJson = metadataJson
            },
            cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ScriptWorkflowBridgeResult<ScriptWorkflowItem>.Succeeded(result.Value!)
            : Failed<ScriptWorkflowItem>(result.Error!);
    }

    /// <summary>
    /// Fails the workflow item for a worker context using the context lease owner.
    /// </summary>
    public static async Task<ScriptWorkflowBridgeResult<ScriptWorkflowItem>> FailItemAsync(
        IScriptWorkflowLedger ledger,
        ScriptWorkflowWorkerContext context,
        string error,
        string? errorType = null,
        DateTimeOffset? nextRetryAt = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await ledger.FailItemAsync(
            context.ItemId,
            new ScriptWorkflowItemFailureOptions
            {
                Error = error,
                ErrorType = errorType,
                LeaseOwner = context.LeaseOwner,
                NextRetryAt = nextRetryAt,
                MetadataJson = metadataJson
            },
            cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ScriptWorkflowBridgeResult<ScriptWorkflowItem>.Succeeded(result.Value!)
            : Failed<ScriptWorkflowItem>(result.Error!);
    }

    /// <summary>
    /// Releases the workflow item for a worker context using the context lease owner.
    /// </summary>
    public static async Task<ScriptWorkflowBridgeResult<ScriptWorkflowItem>> ReleaseItemAsync(
        IScriptWorkflowLedger ledger,
        ScriptWorkflowWorkerContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await ledger.ReleaseItemAsync(
            context.ItemId,
            context.LeaseOwner,
            cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ScriptWorkflowBridgeResult<ScriptWorkflowItem>.Succeeded(result.Value!)
            : Failed<ScriptWorkflowItem>(result.Error!);
    }

    private static ScriptWorkflowBridgeResult<ScriptWorkflowWorkerContext> InvalidContext(string message) =>
        ScriptWorkflowBridgeResult<ScriptWorkflowWorkerContext>.Failed(
            new ScriptWorkflowBridgeError(ScriptWorkflowBridgeErrorCode.InvalidRequest, message));

    private static ScriptWorkflowBridgeResult<T> Failed<T>(ScriptWorkflowLedgerError error) =>
        ScriptWorkflowBridgeResult<T>.Failed(new ScriptWorkflowBridgeError(
            ScriptWorkflowBridgeErrorCode.WorkflowLedgerError,
            error.Message,
            ledgerError: error));

    private static ScriptWorkflowBridgeResult<T> Failed<T>(ScriptWorkerError error) =>
        ScriptWorkflowBridgeResult<T>.Failed(new ScriptWorkflowBridgeError(
            ScriptWorkflowBridgeErrorCode.WorkerError,
            error.Message,
            workerError: error));

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
