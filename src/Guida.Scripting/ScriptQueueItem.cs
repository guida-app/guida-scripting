namespace Guida.Scripting;

/// <summary>
/// One script queue item.
/// </summary>
public sealed record ScriptQueueItem
{
    /// <summary>
    /// Queue item id.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Queue name.
    /// </summary>
    public string QueueName { get; init; } = string.Empty;

    /// <summary>
    /// Queue item payload bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>
    /// Optional payload content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Timestamp when the item was enqueued.
    /// </summary>
    public DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>
    /// Optional timestamp when the item becomes claimable.
    /// </summary>
    public DateTimeOffset? AvailableAt { get; init; }

    /// <summary>
    /// Optional timestamp when the item was last claimed.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; init; }

    /// <summary>
    /// Number of times the item has been claimed.
    /// </summary>
    public int AttemptCount { get; init; }
}
