namespace Guida.Scripting;

/// <summary>
/// Receives log entries emitted by script engines or host adapters.
/// </summary>
public interface IScriptLogger
{
    /// <summary>
    /// Records a script log entry.
    /// </summary>
    void Log(ScriptLogEntry entry);
}
