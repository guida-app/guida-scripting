namespace Guida.Scripting;

/// <summary>
/// Host-provided context available to script engines.
/// </summary>
public sealed record ScriptHostContext
{
    /// <summary>
    /// Empty host context used when a host does not provide runtime services.
    /// </summary>
    public static ScriptHostContext Empty { get; } = new();

    /// <summary>
    /// Execution metadata associated with the current script task.
    /// </summary>
    public ScriptExecutionInfo Execution { get; init; } = ScriptExecutionInfo.Empty;

    /// <summary>
    /// Host-provided capabilities available to script engines and API adapters.
    /// </summary>
    public ScriptHostCapabilities Capabilities { get; init; } = ScriptHostCapabilities.Empty;

    /// <summary>
    /// Optional logger engines can use for script-visible or host-visible messages.
    /// </summary>
    public IScriptLogger? Logger { get; init; }

    /// <summary>
    /// Returns a context with <typeparamref name="TCapability" /> registered.
    /// </summary>
    public ScriptHostContext WithCapability<TCapability>(TCapability capability)
        where TCapability : class, IScriptHostCapability =>
        this with { Capabilities = Capabilities.Set(capability) };

    /// <summary>
    /// Attempts to retrieve a capability registered as <typeparamref name="TCapability" />.
    /// </summary>
    public bool TryGetCapability<TCapability>(out TCapability? capability)
        where TCapability : class, IScriptHostCapability =>
        Capabilities.TryGet(out capability);

    /// <summary>
    /// Gets a capability registered as <typeparamref name="TCapability" />, or <see langword="null" /> when unavailable.
    /// </summary>
    public TCapability? GetCapability<TCapability>()
        where TCapability : class, IScriptHostCapability =>
        Capabilities.Get<TCapability>();
}
