namespace Guida.Scripting;

/// <summary>
/// Stable error codes for expected script worker failures.
/// </summary>
public enum ScriptWorkerErrorCode
{
    /// <summary>
    /// The worker name is invalid.
    /// </summary>
    InvalidWorkerName,

    /// <summary>
    /// The worker job id is invalid.
    /// </summary>
    InvalidJobId,

    /// <summary>
    /// The requested worker job was not found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The requested worker job already exists.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The requested worker job cannot transition from its current state.
    /// </summary>
    InvalidState,

    /// <summary>
    /// The worker capability is unavailable.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The host denied access to the requested worker or job.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// A host I/O failure occurred.
    /// </summary>
    IoError
}
