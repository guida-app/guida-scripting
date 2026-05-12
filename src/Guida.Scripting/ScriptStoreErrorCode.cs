namespace Guida.Scripting;

/// <summary>
/// Stable error codes for expected script store failures.
/// </summary>
public enum ScriptStoreErrorCode
{
    /// <summary>
    /// The store key is invalid.
    /// </summary>
    InvalidKey,

    /// <summary>
    /// The requested store entry was not found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The requested store entry already exists.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The host denied access to the requested store key.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// The store capability is unavailable.
    /// </summary>
    Unavailable,

    /// <summary>
    /// A host I/O failure occurred.
    /// </summary>
    IoError
}
