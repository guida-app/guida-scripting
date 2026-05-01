namespace Guida.Scripting;

/// <summary>
/// Stable error codes for expected script queue failures.
/// </summary>
public enum ScriptQueueErrorCode
{
    /// <summary>
    /// The queue name is invalid.
    /// </summary>
    InvalidQueueName,

    /// <summary>
    /// The queue item id is invalid.
    /// </summary>
    InvalidItemId,

    /// <summary>
    /// The requested queue item was not found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The requested queue item already exists.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The queue capability is unavailable.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The host denied access to the requested queue or item.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// A host I/O failure occurred.
    /// </summary>
    IoError
}
