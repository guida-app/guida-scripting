namespace Guida.Scripting;

/// <summary>
/// Public snapshot of a host-managed worker job.
/// </summary>
public sealed record ScriptWorkerJob
{
    /// <summary>
    /// Worker job id.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Worker name.
    /// </summary>
    public string WorkerName { get; init; } = string.Empty;

    /// <summary>
    /// Current worker job status.
    /// </summary>
    public ScriptWorkerStatus Status { get; init; } = ScriptWorkerStatus.Pending;

    /// <summary>
    /// Worker job payload bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>
    /// Optional payload content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Optional host correlation id.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Optional source queue name.
    /// </summary>
    public string? SourceQueueName { get; init; }

    /// <summary>
    /// Optional source queue item id.
    /// </summary>
    public string? SourceQueueItemId { get; init; }

    /// <summary>
    /// Origin used when the job starts script work.
    /// </summary>
    public ScriptTaskOrigin Origin { get; init; } = ScriptTaskOrigin.Worker;

    /// <summary>
    /// Timestamp when the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Optional timestamp when the job started running.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// Optional timestamp when the job reached a terminal state.
    /// </summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>
    /// Optional associated script task id.
    /// </summary>
    public string? TaskId { get; init; }

    /// <summary>
    /// Error text for failed or canceled jobs.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Values returned by host-managed worker work.
    /// </summary>
    public IReadOnlyList<object?> ReturnValues { get; init; } = Array.Empty<object?>();
}
