using System.Net.Http;

namespace Guida.Scripting;

/// <summary>
/// Host-mediated HTTP client capability for scripts and API adapters.
/// </summary>
public interface IScriptHttpClient : IScriptHostCapability
{
    /// <summary>
    /// Sends an HTTP request through the host-provided HTTP boundary.
    /// </summary>
    Task<ScriptHttpResult<HttpResponseMessage>> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default);
}
