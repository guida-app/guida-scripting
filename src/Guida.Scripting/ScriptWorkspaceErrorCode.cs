namespace Guida.Scripting;

/// <summary>
/// Stable error codes for expected workspace failures.
/// </summary>
public enum ScriptWorkspaceErrorCode
{
    /// <summary>
    /// The logical workspace path is invalid.
    /// </summary>
    InvalidPath,

    /// <summary>
    /// The requested workspace entry was not found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The requested entry already exists.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The requested entry is not a file.
    /// </summary>
    NotAFile,

    /// <summary>
    /// The requested entry is not a directory.
    /// </summary>
    NotADirectory,

    /// <summary>
    /// The host denied access to the requested workspace path.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// The workspace is read-only.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// A host I/O failure occurred.
    /// </summary>
    IoError
}
