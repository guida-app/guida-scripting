using System.Text.Json;

namespace Guida.Scripting;

/// <summary>
/// Stable error codes for workflow queue and worker bridge failures.
/// </summary>
public enum ScriptWorkflowBridgeErrorCode
{
    /// <summary>The bridge request is invalid.</summary>
    InvalidRequest,

    /// <summary>The workflow queue envelope is invalid.</summary>
    InvalidEnvelope,

    /// <summary>A workflow ledger operation failed.</summary>
    WorkflowLedgerError,

    /// <summary>A queue operation failed.</summary>
    QueueError,

    /// <summary>A worker operation failed.</summary>
    WorkerError
}

/// <summary>
/// Describes an expected workflow bridge operation failure.
/// </summary>
public sealed record ScriptWorkflowBridgeError
{
    public ScriptWorkflowBridgeError(
        ScriptWorkflowBridgeErrorCode code,
        string message,
        ScriptWorkflowLedgerError? ledgerError = null,
        ScriptQueueError? queueError = null,
        ScriptWorkerError? workerError = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
        LedgerError = ledgerError;
        QueueError = queueError;
        WorkerError = workerError;
    }

    /// <summary>Stable workflow bridge error code.</summary>
    public ScriptWorkflowBridgeErrorCode Code { get; }

    /// <summary>Host-readable error message.</summary>
    public string Message { get; }

    /// <summary>Underlying workflow ledger error when one caused the bridge failure.</summary>
    public ScriptWorkflowLedgerError? LedgerError { get; }

    /// <summary>Underlying queue error when one caused the bridge failure.</summary>
    public ScriptQueueError? QueueError { get; }

    /// <summary>Underlying worker error when one caused the bridge failure.</summary>
    public ScriptWorkerError? WorkerError { get; }

    /// <summary>
    /// Converts the workflow bridge error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}

/// <summary>
/// Result of a workflow bridge operation.
/// </summary>
public sealed record ScriptWorkflowBridgeResult<T>
{
    private ScriptWorkflowBridgeResult(bool success, T? value, ScriptWorkflowBridgeError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>Whether the workflow bridge operation succeeded.</summary>
    public bool Success { get; }

    /// <summary>Returned value when the operation succeeded.</summary>
    public T? Value { get; }

    /// <summary>Error information when the operation failed.</summary>
    public ScriptWorkflowBridgeError? Error { get; }

    /// <summary>Creates a successful workflow bridge result.</summary>
    public static ScriptWorkflowBridgeResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>Creates a failed workflow bridge result.</summary>
    public static ScriptWorkflowBridgeResult<T> Failed(ScriptWorkflowBridgeError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ScriptWorkflowBridgeResult<T>(false, default, error);
    }
}

/// <summary>
/// Request to upsert a workflow item and enqueue it for host-owned processing.
/// </summary>
public sealed record ScriptWorkflowQueueEnqueueRequest
{
    public string? WorkflowName { get; init; }
    public string? ItemKey { get; init; }
    public string? ItemType { get; init; }
    public string? RunId { get; init; }
    public string? Stage { get; init; }
    public string? State { get; init; } = "pending";
    public int Priority { get; init; }
    public int? MaxAttempts { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public string? QueueName { get; init; }
    public string? QueueItemId { get; init; }
    public ReadOnlyMemory<byte> Payload { get; init; }
    public string? PayloadContentType { get; init; }
    public DateTimeOffset? AvailableAt { get; init; }
    public TimeSpan? Delay { get; init; }
    public string? MetadataJson { get; init; }
    public string? EventType { get; init; } = "enqueued";
    public string? EventIdempotencyKey { get; init; }
    public string? EventMetadataJson { get; init; }
}

/// <summary>
/// JSON payload placed onto a queue for workflow-owned work.
/// </summary>
public sealed record ScriptWorkflowQueueEnvelope
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int Version { get; init; } = 1;
    public string WorkflowName { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public string ItemKey { get; init; } = string.Empty;
    public string? ItemType { get; init; }
    public string? RunId { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int Priority { get; init; }
    public int AttemptCount { get; init; }
    public int? MaxAttempts { get; init; }
    public string? PayloadContentType { get; init; }
    public byte[] Payload { get; init; } = [];
    public string? MetadataJson { get; init; }

    /// <summary>Serializes the envelope as UTF-8 JSON.</summary>
    public byte[] ToJsonBytes() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    /// <summary>Deserializes a workflow queue envelope from UTF-8 JSON.</summary>
    public static ScriptWorkflowBridgeResult<ScriptWorkflowQueueEnvelope> FromJson(ReadOnlyMemory<byte> payload)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<ScriptWorkflowQueueEnvelope>(payload.Span, JsonOptions);
            if (envelope == null)
            {
                return ScriptWorkflowBridgeResult<ScriptWorkflowQueueEnvelope>.Failed(InvalidEnvelope("Workflow queue envelope is empty."));
            }

            if (envelope.Version != 1)
            {
                return ScriptWorkflowBridgeResult<ScriptWorkflowQueueEnvelope>.Failed(InvalidEnvelope("Workflow queue envelope version is not supported."));
            }

            if (string.IsNullOrWhiteSpace(envelope.WorkflowName) ||
                string.IsNullOrWhiteSpace(envelope.ItemId) ||
                string.IsNullOrWhiteSpace(envelope.ItemKey) ||
                string.IsNullOrWhiteSpace(envelope.Stage) ||
                string.IsNullOrWhiteSpace(envelope.State))
            {
                return ScriptWorkflowBridgeResult<ScriptWorkflowQueueEnvelope>.Failed(InvalidEnvelope("Workflow queue envelope is missing required item fields."));
            }

            return ScriptWorkflowBridgeResult<ScriptWorkflowQueueEnvelope>.Succeeded(envelope);
        }
        catch (JsonException ex)
        {
            return ScriptWorkflowBridgeResult<ScriptWorkflowQueueEnvelope>.Failed(InvalidEnvelope($"Workflow queue envelope JSON is invalid: {ex.Message}"));
        }
    }

    private static ScriptWorkflowBridgeError InvalidEnvelope(string message) =>
        new(ScriptWorkflowBridgeErrorCode.InvalidEnvelope, message);
}

public sealed record ScriptWorkflowQueueEnqueueResult(
    ScriptWorkflowItem Item,
    ScriptQueueItem QueueItem,
    ScriptWorkflowEvent Event,
    ScriptWorkflowQueueEnvelope Envelope,
    bool QueueItemAlreadyExisted);

public sealed record ScriptWorkflowWorkerDispatchOptions
{
    public string? JobId { get; init; }
    public ScriptTaskOrigin Origin { get; init; } = ScriptTaskOrigin.Queue;
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Workflow context carried by a host worker while processing one workflow item.
/// </summary>
public sealed record ScriptWorkflowWorkerContext
{
    public string WorkerName { get; init; } = string.Empty;
    public string JobId { get; init; } = string.Empty;
    public string? TaskId { get; init; }
    public string? SourceQueueName { get; init; }
    public string? SourceQueueItemId { get; init; }
    public string WorkflowName { get; init; } = string.Empty;
    public string? RunId { get; init; }
    public string ItemId { get; init; } = string.Empty;
    public string ItemKey { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Lease owner to use for workflow ledger operations. Running task ids are preferred over worker job ids.
    /// </summary>
    public string LeaseOwner => string.IsNullOrWhiteSpace(TaskId) ? JobId : TaskId!;
}
