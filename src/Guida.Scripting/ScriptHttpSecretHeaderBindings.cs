using System.Net.Http;

namespace Guida.Scripting;

/// <summary>
/// Helpers for attaching secret-backed header bindings to HTTP requests.
/// </summary>
public static class ScriptHttpSecretHeaderBindings
{
    /// <summary>
    /// Request option key that carries secret-backed HTTP header bindings.
    /// </summary>
    public static HttpRequestOptionsKey<IReadOnlyList<ScriptHttpSecretHeaderBinding>> OptionKey { get; } =
        new("Guida.Scripting.Http.SecretHeaderBindings");

    /// <summary>
    /// Attaches secret-backed header bindings to a request.
    /// </summary>
    public static void Set(
        HttpRequestMessage request,
        IReadOnlyList<ScriptHttpSecretHeaderBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bindings);

        request.Options.Set(OptionKey, bindings);
    }

    /// <summary>
    /// Gets secret-backed header bindings attached to a request.
    /// </summary>
    public static IReadOnlyList<ScriptHttpSecretHeaderBinding> Get(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Options.TryGetValue(OptionKey, out var bindings)
            ? bindings
            : Array.Empty<ScriptHttpSecretHeaderBinding>();
    }
}
