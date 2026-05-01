namespace Guida.Scripting;

/// <summary>
/// Describes the lifecycle status of a script task.
/// </summary>
public enum ScriptTaskStatus
{
    /// <summary>
    /// The task is currently running.
    /// </summary>
    Running = 0,

    /// <summary>
    /// The task completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The task failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The task was canceled.
    /// </summary>
    Canceled,

    /// <summary>
    /// The task timed out.
    /// </summary>
    TimedOut
}
