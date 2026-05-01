namespace Guida.Scripting;

/// <summary>
/// Host-mediated search capability for host-owned indexes or providers.
/// </summary>
public interface IScriptSearch : IScriptHostCapability
{
    /// <summary>
    /// Searches host-owned content.
    /// </summary>
    Task<ScriptSearchResult<ScriptSearchResponse>> SearchAsync(
        ScriptSearchRequest request,
        CancellationToken cancellationToken = default);
}
