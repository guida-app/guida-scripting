namespace Guida.Scripting;

/// <summary>
/// Discovers workflow layout information from a host-owned script workspace.
/// </summary>
public interface IScriptWorkflowWorkspaceDiscovery : IScriptHostCapability
{
    /// <summary>
    /// Discovers global and workflow-scoped files using the SDK workflow workspace layout.
    /// </summary>
    Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSnapshot>> DiscoverAsync(
        IScriptWorkspace workspace,
        ScriptWorkflowWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads workflow ledger schema JSON files from discovered workflow folders.
    /// </summary>
    Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceLedgerSchemas>> LoadLedgerSchemasAsync(
        IScriptWorkspace workspace,
        ScriptWorkflowWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default);
}
