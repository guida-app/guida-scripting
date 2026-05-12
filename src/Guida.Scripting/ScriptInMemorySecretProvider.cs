using System.Collections.ObjectModel;

namespace Guida.Scripting;

/// <summary>
/// In-memory secret provider for tests, samples, and simple hosts.
/// </summary>
public sealed class ScriptInMemorySecretProvider : IScriptSecretProvider
{
    private readonly IReadOnlyDictionary<string, string> _secrets;

    /// <summary>
    /// Creates an in-memory provider from explicit secret values.
    /// </summary>
    public ScriptInMemorySecretProvider(IReadOnlyDictionary<string, string> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        _secrets = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(secrets, StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public Task<ScriptSecretResult<ScriptSecret>> GetSecretAsync(
        ScriptSecretReference reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (reference is null || string.IsNullOrWhiteSpace(reference.Name))
        {
            return Task.FromResult(Failed(
                ScriptSecretErrorCode.InvalidName,
                reference?.Name ?? string.Empty,
                "Secret name cannot be empty."));
        }

        if (!_secrets.TryGetValue(reference.Name, out var value))
        {
            return Task.FromResult(Failed(
                ScriptSecretErrorCode.NotFound,
                reference.Name,
                $"Secret '{reference.Name}' was not found."));
        }

        return Task.FromResult(ScriptSecretResult<ScriptSecret>.Succeeded(new ScriptSecret
        {
            Name = reference.Name,
            Value = value
        }));
    }

    private static ScriptSecretResult<ScriptSecret> Failed(
        ScriptSecretErrorCode code,
        string name,
        string message) =>
        ScriptSecretResult<ScriptSecret>.Failed(new ScriptSecretError(code, name, message));
}
