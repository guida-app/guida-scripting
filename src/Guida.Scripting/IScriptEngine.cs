namespace Guida.Scripting;

/// <summary>
/// Executes scripts for a specific scripting runtime.
/// </summary>
public interface IScriptEngine : IDisposable
{
    /// <summary>
    /// Executes a script request and returns its result.
    /// </summary>
    Task<ScriptExecutionResult> ExecuteAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests immediate termination of the running script when supported by the engine.
    /// </summary>
    void Stop();
}
