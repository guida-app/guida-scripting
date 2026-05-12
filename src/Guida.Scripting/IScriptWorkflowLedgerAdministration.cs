namespace Guida.Scripting;

/// <summary>
/// Optional workflow ledger administration and read-model capability.
/// </summary>
public interface IScriptWorkflowLedgerAdministration : IScriptHostCapability
{
    /// <summary>
    /// Previews retention pruning without deleting history.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerRetentionResult>> PreviewRetentionAsync(
        ScriptWorkflowLedgerRetentionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes old terminal workflow ledger history.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerRetentionResult>> PruneRetentionAsync(
        ScriptWorkflowLedgerRetentionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports workflow ledger history as a portable model payload.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerExport>> ExportHistoryAsync(
        ScriptWorkflowLedgerExportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports workflow ledger history from a portable model payload.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerImportResult>> ImportHistoryAsync(
        ScriptWorkflowLedgerExport export,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregate ledger counts and attention items.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerOverview>> GetOverviewAsync(
        ScriptWorkflowLedgerOverviewQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets observed workflow transition graph data, optionally overlaid with schema information.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowTransitionGraph>> GetTransitionGraphAsync(
        ScriptWorkflowTransitionGraphQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets flow evidence grouped from workflow ledger queue metadata.
    /// </summary>
    Task<ScriptWorkflowLedgerResult<ScriptWorkflowFlowEvidence>> GetFlowEvidenceAsync(
        ScriptWorkflowFlowEvidenceQuery query,
        CancellationToken cancellationToken = default);
}
