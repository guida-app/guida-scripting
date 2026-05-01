namespace Guida.Scripting;

/// <summary>
/// Stable error codes for expected script search failures.
/// </summary>
public enum ScriptSearchErrorCode
{
    /// <summary>
    /// The query is invalid.
    /// </summary>
    InvalidQuery,

    /// <summary>
    /// The search scope is invalid.
    /// </summary>
    InvalidScope,

    /// <summary>
    /// The search capability is unavailable.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The host denied access to the requested search scope.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// The host search provider failed.
    /// </summary>
    ProviderError
}
