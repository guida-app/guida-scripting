namespace Guida.Scripting;

/// <summary>
/// Severity of a script log entry.
/// </summary>
public enum ScriptLogLevel
{
    /// <summary>
    /// Trace-level diagnostic information.
    /// </summary>
    Trace = 0,

    /// <summary>
    /// Debug diagnostic information.
    /// </summary>
    Debug,

    /// <summary>
    /// Informational message.
    /// </summary>
    Information,

    /// <summary>
    /// Warning message.
    /// </summary>
    Warning,

    /// <summary>
    /// Error message.
    /// </summary>
    Error
}
