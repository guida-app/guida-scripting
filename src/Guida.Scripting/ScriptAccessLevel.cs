namespace Guida.Scripting;

/// <summary>
/// Describes the trust level assigned to a script execution.
/// </summary>
public enum ScriptAccessLevel
{
    /// <summary>
    /// The script execution is trusted by the host.
    /// </summary>
    Trusted = 0,

    /// <summary>
    /// The script execution is allowed limited host-mediated access.
    /// </summary>
    Restricted,

    /// <summary>
    /// The script execution is not allowed sensitive host-mediated access.
    /// </summary>
    Untrusted
}
