namespace Guida.Scripting;

/// <summary>
/// Host-mediated queue available to script engines and API adapters.
/// </summary>
public interface IScriptQueue : IScriptHostCapability
{
    /// <summary>
    /// Enqueues one item.
    /// </summary>
    Task<ScriptQueueResult<ScriptQueueItem>> EnqueueAsync(
        string queueName,
        ReadOnlyMemory<byte> payload,
        ScriptQueueEnqueueOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims available items using the queue implementation's dequeue strategy.
    /// </summary>
    Task<ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>> ClaimAsync(
        string queueName,
        ScriptQueueClaimOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a claimed or queued item.
    /// </summary>
    Task<ScriptQueueResult> CompleteAsync(
        string queueName,
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons a claimed item so it can be claimed again.
    /// </summary>
    Task<ScriptQueueResult> AbandonAsync(
        string queueName,
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one queue item by id.
    /// </summary>
    Task<ScriptQueueResult<ScriptQueueItem>> GetAsync(
        string queueName,
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists current queue items.
    /// </summary>
    Task<ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>> ListAsync(
        string queueName,
        CancellationToken cancellationToken = default);
}
