namespace Guida.Scripting;

/// <summary>
/// Stable error codes for expected HTTP capability failures.
/// </summary>
public enum ScriptHttpErrorCode
{
    /// <summary>
    /// The request is malformed or incomplete.
    /// </summary>
    InvalidRequest,

    /// <summary>
    /// The request was denied by host HTTP policy.
    /// </summary>
    BlockedByPolicy,

    /// <summary>
    /// The request timed out.
    /// </summary>
    Timeout,

    /// <summary>
    /// The HTTP transport failed.
    /// </summary>
    NetworkError,

    /// <summary>
    /// The response exceeded the configured response size limit.
    /// </summary>
    ResponseTooLarge,

    /// <summary>
    /// The response could not be represented by the HTTP capability.
    /// </summary>
    UnsupportedResponse
}
