using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Guida.Scripting;

/// <summary>
/// Immutable set of host-provided capabilities keyed by their public capability type.
/// </summary>
public sealed class ScriptHostCapabilities
{
    private readonly IReadOnlyDictionary<Type, IScriptHostCapability> _capabilities;

    private ScriptHostCapabilities(IReadOnlyDictionary<Type, IScriptHostCapability> capabilities)
    {
        _capabilities = capabilities;
    }

    /// <summary>
    /// Empty capability set used when a host does not provide capabilities.
    /// </summary>
    public static ScriptHostCapabilities Empty { get; } =
        new(new ReadOnlyDictionary<Type, IScriptHostCapability>(
            new Dictionary<Type, IScriptHostCapability>()));

    /// <summary>
    /// Returns a new capability set with <typeparamref name="TCapability" /> registered.
    /// </summary>
    public ScriptHostCapabilities Set<TCapability>(TCapability capability)
        where TCapability : class, IScriptHostCapability
    {
        ArgumentNullException.ThrowIfNull(capability);

        var capabilities = new Dictionary<Type, IScriptHostCapability>(_capabilities)
        {
            [typeof(TCapability)] = capability
        };

        return new ScriptHostCapabilities(
            new ReadOnlyDictionary<Type, IScriptHostCapability>(capabilities));
    }

    /// <summary>
    /// Attempts to retrieve a capability registered as <typeparamref name="TCapability" />.
    /// </summary>
    public bool TryGet<TCapability>([NotNullWhen(true)] out TCapability? capability)
        where TCapability : class, IScriptHostCapability
    {
        if (_capabilities.TryGetValue(typeof(TCapability), out var value) &&
            value is TCapability typed)
        {
            capability = typed;
            return true;
        }

        capability = null;
        return false;
    }

    /// <summary>
    /// Gets a capability registered as <typeparamref name="TCapability" />, or <see langword="null" /> when unavailable.
    /// </summary>
    public TCapability? Get<TCapability>()
        where TCapability : class, IScriptHostCapability =>
        TryGet<TCapability>(out var capability) ? capability : null;

    /// <summary>
    /// Returns a new capability set without <typeparamref name="TCapability" />.
    /// </summary>
    public ScriptHostCapabilities Remove<TCapability>()
        where TCapability : class, IScriptHostCapability
    {
        if (!_capabilities.ContainsKey(typeof(TCapability)))
        {
            return this;
        }

        var capabilities = new Dictionary<Type, IScriptHostCapability>(_capabilities);
        capabilities.Remove(typeof(TCapability));

        return capabilities.Count == 0
            ? Empty
            : new ScriptHostCapabilities(
                new ReadOnlyDictionary<Type, IScriptHostCapability>(capabilities));
    }
}
