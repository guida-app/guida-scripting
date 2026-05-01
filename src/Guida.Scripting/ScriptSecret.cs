namespace Guida.Scripting;

/// <summary>
/// Secret value returned to host-side adapters and wrappers.
/// </summary>
public sealed record ScriptSecret
{
    /// <summary>
    /// Host-defined secret name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Secret value. This should not be exposed through script-facing APIs.
    /// </summary>
    public string Value { get; init; } = string.Empty;
}
