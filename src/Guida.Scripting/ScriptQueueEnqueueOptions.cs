namespace Guida.Scripting;

/// <summary>
/// Options used when enqueueing an item.
/// </summary>
public sealed record ScriptQueueEnqueueOptions
{
    /// <summary>
    /// Optional caller-provided item id.
    /// </summary>
    public string? ItemId { get; init; }

    /// <summary>
    /// Optional payload content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Optional timestamp when the item should become claimable.
    /// </summary>
    public DateTimeOffset? AvailableAt { get; init; }

    /// <summary>
    /// Optional delay before the item should become claimable.
    /// </summary>
    public TimeSpan? Delay { get; init; }
}
