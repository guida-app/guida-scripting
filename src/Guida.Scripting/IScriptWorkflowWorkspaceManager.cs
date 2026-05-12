namespace Guida.Scripting;

/// <summary>
/// Host-neutral workflow workspace management and read-model capability.
/// </summary>
public interface IScriptWorkflowWorkspaceManager : IScriptHostCapability
{
    /// <summary>
    /// Name of the active workflow, or null when only global workspace configuration is active.
    /// </summary>
    string? ActiveWorkflowName { get; }

    /// <summary>
    /// Raised when the active workflow changes.
    /// </summary>
    event EventHandler<ScriptWorkflowWorkspaceActivation>? ActiveWorkflowChanged;

    /// <summary>
    /// Lists discovered workflows with active-workflow read-model metadata.
    /// </summary>
    Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceReadModel>> ListAsync(
        IScriptWorkspace workspace,
        ScriptWorkflowWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a read-only inspection model for one workflow.
    /// </summary>
    Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceInspection>> InspectAsync(
        IScriptWorkspace workspace,
        string workflowName,
        ScriptWorkflowWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Summarizes the workflow workspace.
    /// </summary>
    Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSummary>> SummarizeAsync(
        IScriptWorkspace workspace,
        ScriptWorkflowWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates a workflow by name, or deactivates workflow overlays when <paramref name="workflowName" /> is null or empty.
    /// </summary>
    Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceActivation>> ActivateAsync(
        IScriptWorkspace workspace,
        string? workflowName,
        ScriptWorkflowWorkspaceActivationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a workflow folder with a manifest.
    /// </summary>
    Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowDefinition>> CreateAsync(
        IScriptWorkspace workspace,
        string workflowName,
        ScriptWorkflowWorkspaceCreateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables a workflow by updating its manifest.
    /// </summary>
    Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowDefinition>> SetEnabledAsync(
        IScriptWorkspace workspace,
        string workflowName,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports all files under a workflow folder into a portable payload.
    /// </summary>
    Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceExport>> ExportAsync(
        IScriptWorkspace workspace,
        string workflowName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a portable workflow payload into a workflow folder.
    /// </summary>
    Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceImportResult>> ImportAsync(
        IScriptWorkspace workspace,
        ScriptWorkflowWorkspaceExport export,
        ScriptWorkflowWorkspaceImportOptions? options = null,
        CancellationToken cancellationToken = default);
}
