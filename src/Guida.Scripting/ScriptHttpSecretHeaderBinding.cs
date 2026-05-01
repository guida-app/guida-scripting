namespace Guida.Scripting;

/// <summary>
/// Describes a secret-backed HTTP header to apply before sending a request.
/// </summary>
public sealed record ScriptHttpSecretHeaderBinding
{
    /// <summary>
    /// HTTP header name to set.
    /// </summary>
    public string HeaderName { get; init; } = string.Empty;

    /// <summary>
    /// Secret reference used as the header value source.
    /// </summary>
    public ScriptSecretReference Secret { get; init; } = new();

    /// <summary>
    /// Optional value prefix, such as "Bearer ".
    /// </summary>
    public string ValuePrefix { get; init; } = string.Empty;

    /// <summary>
    /// Whether an existing header value may be replaced.
    /// </summary>
    public bool ReplaceExisting { get; init; }
}
