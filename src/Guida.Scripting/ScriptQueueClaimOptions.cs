namespace Guida.Scripting;

/// <summary>
/// Options used when claiming queue items.
/// </summary>
public sealed record ScriptQueueClaimOptions
{
    /// <summary>
    /// Maximum number of items to claim.
    /// </summary>
    public int MaxItemCount { get; init; } = 1;

    /// <summary>
    /// Optional duration that claimed items should be hidden from future claims.
    /// </summary>
    public TimeSpan? VisibilityTimeout { get; init; }

    /// <summary>
    /// Optional host-defined dequeue strategy name.
    /// </summary>
    public string? StrategyName { get; init; }
}
