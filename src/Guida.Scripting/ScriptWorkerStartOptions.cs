namespace Guida.Scripting;

/// <summary>
/// Options used when starting host-managed worker work.
/// </summary>
public sealed record ScriptWorkerStartOptions
{
    /// <summary>
    /// Optional caller-provided job id.
    /// </summary>
    public string? JobId { get; init; }

    /// <summary>
    /// Origin to use for associated script work.
    /// </summary>
    public ScriptTaskOrigin Origin { get; init; } = ScriptTaskOrigin.Worker;
}
