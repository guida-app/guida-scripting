namespace Guida.Scripting;

/// <summary>
/// Host-mediated provider for named script secrets.
/// </summary>
public interface IScriptSecretProvider : IScriptHostCapability
{
    /// <summary>
    /// Gets one named secret.
    /// </summary>
    Task<ScriptSecretResult<ScriptSecret>> GetSecretAsync(
        ScriptSecretReference reference,
        CancellationToken cancellationToken = default);
}
