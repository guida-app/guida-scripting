namespace Guida.Scripting;

/// <summary>
/// Stable error codes for expected document loading failures.
/// </summary>
public enum ScriptDocumentErrorCode
{
    /// <summary>
    /// The document identifier is invalid.
    /// </summary>
    InvalidId,

    /// <summary>
    /// The requested document was not found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The requested document is not a file.
    /// </summary>
    NotAFile,

    /// <summary>
    /// The host denied access to the document.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// The document encoding is unsupported or invalid.
    /// </summary>
    UnsupportedEncoding,

    /// <summary>
    /// A host I/O failure occurred.
    /// </summary>
    IoError
}
