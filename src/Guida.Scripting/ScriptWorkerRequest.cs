namespace Guida.Scripting;

/// <summary>
/// Request to start host-managed worker work.
/// </summary>
public sealed record ScriptWorkerRequest
{
    /// <summary>
    /// Worker name.
    /// </summary>
    public string WorkerName { get; init; } = string.Empty;

    /// <summary>
    /// Worker payload bytes.
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
}
