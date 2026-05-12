namespace Guida.Scripting;

/// <summary>
/// Host-mediated workflow ledger available to script engines and API adapters.
/// </summary>
public interface IScriptWorkflowLedger : IScriptHostCapability
{
    /// <summary>
    /// Starts a workflow run.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> StartRunAsync(
        string workflowName,
        ScriptWorkflowRunOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one workflow run by id.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> GetRunAsync(
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists workflow runs.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowRun>>> ListRunsAsync(
        ScriptWorkflowRunQuery? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a workflow run completed.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> FinishRunAsync(
        string runId,
        ScriptWorkflowRunFinishOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a workflow run failed.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> FailRunAsync(
        string runId,
        ScriptWorkflowRunFailOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a workflow run cancelled.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> CancelRunAsync(
        string runId,
        ScriptWorkflowRunCancelOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a workflow item.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> UpsertItemAsync(
        ScriptWorkflowItemUpsert input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one workflow item by workflow name and item key.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> GetItemAsync(
        string workflowName,
        string itemKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one workflow item by id.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> GetItemByIdAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries workflow items.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>> QueryItemsAsync(
        ScriptWorkflowItemQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a workflow item's state projection.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> SetItemStateAsync(
        string itemId,
        ScriptWorkflowStateUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an event to a workflow item.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowEvent>> AppendEventAsync(
        string itemId,
        ScriptWorkflowEventAppend input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists events for a workflow item.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowEvent>>> GetEventsForItemAsync(
        string itemId,
        ScriptWorkflowEventQuery? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches an artifact reference to a workflow item.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowArtifact>> AttachArtifactAsync(
        string itemId,
        ScriptWorkflowArtifactAttach input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists artifact references for a workflow item.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowArtifact>>> GetArtifactsForItemAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a specific workflow item.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> ClaimItemAsync(
        string itemId,
        ScriptWorkflowClaimOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims eligible pending, retry-ready, or expired-lease workflow items.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>> ClaimNextAsync(
        ScriptWorkflowItemQuery query,
        ScriptWorkflowClaimOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a workflow item completed.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> CompleteItemAsync(
        string itemId,
        ScriptWorkflowItemCompleteOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a workflow item failed or retry-ready according to attempt limits.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> FailItemAsync(
        string itemId,
        ScriptWorkflowItemFailureOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a workflow item lease without recording failure.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> ReleaseItemAsync(
        string itemId,
        string leaseOwner,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a workflow item back to a claimable state.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> RetryItemAsync(
        string itemId,
        DateTimeOffset? nextRetryAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a workflow item dead with a reason.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> DeadLetterItemAsync(
        string itemId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries multiple workflow items.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>> BulkRetryItemsAsync(
        ScriptWorkflowBulkMutationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels multiple workflow items.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>> BulkCancelItemsAsync(
        ScriptWorkflowBulkMutationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dead-letters multiple workflow items.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>> BulkDeadLetterItemsAsync(
        ScriptWorkflowBulkMutationRequest request,
        CancellationToken cancellationToken = default);
}
