namespace Guida.Scripting;

/// <summary>
/// Host policy metadata for a script execution.
/// </summary>
public sealed record ScriptExecutionPolicy
{
    /// <summary>
    /// Default trusted policy.
    /// </summary>
    public static ScriptExecutionPolicy Trusted { get; } = new()
    {
        AccessLevel = ScriptAccessLevel.Trusted,
        AllowRawSecretAccess = true,
        AllowSecretHttpHeaders = true
    };

    /// <summary>
    /// Default restricted policy.
    /// </summary>
    public static ScriptExecutionPolicy Restricted { get; } = new()
    {
        AccessLevel = ScriptAccessLevel.Restricted,
        AllowRawSecretAccess = false,
        AllowSecretHttpHeaders = true
    };

    /// <summary>
    /// Default untrusted policy.
    /// </summary>
    public static ScriptExecutionPolicy Untrusted { get; } = new()
    {
        AccessLevel = ScriptAccessLevel.Untrusted,
        AllowRawSecretAccess = false,
        AllowSecretHttpHeaders = false
    };

    /// <summary>
    /// Trust level for this execution.
    /// </summary>
    public ScriptAccessLevel AccessLevel { get; init; } = ScriptAccessLevel.Trusted;

    /// <summary>
    /// Whether script-facing adapters may expose raw secret values.
    /// </summary>
    public bool AllowRawSecretAccess { get; init; } = true;

    /// <summary>
    /// Whether host-side HTTP adapters may inject secret-backed headers.
    /// </summary>
    public bool AllowSecretHttpHeaders { get; init; } = true;

    /// <summary>
    /// Gets the default execution policy for a task origin.
    /// </summary>
    public static ScriptExecutionPolicy ForOrigin(ScriptTaskOrigin origin) =>
        origin is ScriptTaskOrigin.Mcp or ScriptTaskOrigin.External
            ? Restricted
            : Trusted;
}
