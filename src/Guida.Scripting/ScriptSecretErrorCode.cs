namespace Guida.Scripting;

/// <summary>
/// Stable error codes for expected secret lookup failures.
/// </summary>
public enum ScriptSecretErrorCode
{
    /// <summary>
    /// The secret name is invalid.
    /// </summary>
    InvalidName,

    /// <summary>
    /// The named secret was not found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The host denied access to the named secret.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// The secret provider is unavailable.
    /// </summary>
    Unavailable
}
