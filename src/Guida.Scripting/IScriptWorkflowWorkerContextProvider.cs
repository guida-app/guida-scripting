namespace Guida.Scripting;

/// <summary>
/// Provides workflow ledger context for the currently executing host-managed worker item.
/// </summary>
public interface IScriptWorkflowWorkerContextProvider : IScriptHostCapability
{
    /// <summary>
    /// Current worker workflow context, or null when the current script is not processing workflow-owned work.
    /// </summary>
    ScriptWorkflowWorkerContext? Current { get; }
}
