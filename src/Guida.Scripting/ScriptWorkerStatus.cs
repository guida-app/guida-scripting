namespace Guida.Scripting;

/// <summary>
/// Current status of a host-managed worker job.
/// </summary>
public enum ScriptWorkerStatus
{
    /// <summary>
    /// The job has been accepted but is not running.
    /// </summary>
    Pending,

    /// <summary>
    /// The job is running.
    /// </summary>
    Running,

    /// <summary>
    /// The job completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The job failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The job was canceled.
    /// </summary>
    Canceled
}
