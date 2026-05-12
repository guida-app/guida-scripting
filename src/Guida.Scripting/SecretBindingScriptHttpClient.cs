using System.Net.Http;

namespace Guida.Scripting;

/// <summary>
/// HTTP client wrapper that resolves secret-backed header bindings immediately before sending.
/// </summary>
public sealed class SecretBindingScriptHttpClient : IScriptHttpClient
{
    private readonly IScriptHttpClient _inner;
    private readonly IScriptSecretProvider _secretProvider;

    /// <summary>
    /// Creates a secret-binding HTTP client wrapper.
    /// </summary>
    public SecretBindingScriptHttpClient(
        IScriptHttpClient inner,
        IScriptSecretProvider secretProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(secretProvider);

        _inner = inner;
        _secretProvider = secretProvider;
    }

    /// <inheritdoc />
    public async Task<ScriptHttpResult<HttpResponseMessage>> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var binding in ScriptHttpSecretHeaderBindings.Get(request))
        {
            var applied = await ApplyBindingAsync(request, binding, cancellationToken).ConfigureAwait(false);
            if (!applied.Success)
            {
                return ScriptHttpResult<HttpResponseMessage>.Failed(applied.Error!);
            }
        }

        return await _inner.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScriptHttpResult> ApplyBindingAsync(
        HttpRequestMessage request,
        ScriptHttpSecretHeaderBinding binding,
        CancellationToken cancellationToken)
    {
        if (binding is null)
        {
            return Failed(
                ScriptHttpErrorCode.InvalidRequest,
                request.RequestUri,
                "HTTP secret header binding cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(binding.HeaderName))
        {
            return Failed(
                ScriptHttpErrorCode.InvalidRequest,
                request.RequestUri,
                "HTTP secret header name cannot be empty.");
        }

        try
        {
            if (request.Headers.Contains(binding.HeaderName) && !binding.ReplaceExisting)
            {
                return ScriptHttpResult.Succeeded();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Failed(
                ScriptHttpErrorCode.InvalidRequest,
                request.RequestUri,
                $"HTTP secret header '{binding.HeaderName}' is invalid: {exception.Message}");
        }

        var secret = await _secretProvider
            .GetSecretAsync(binding.Secret, cancellationToken)
            .ConfigureAwait(false);
        if (!secret.Success)
        {
            return ScriptHttpResult.Failed(MapSecretError(request.RequestUri, secret.Error!));
        }

        var headerValue = $"{binding.ValuePrefix}{secret.Value!.Value}";

        try
        {
            if (binding.ReplaceExisting)
            {
                request.Headers.Remove(binding.HeaderName);
            }

            request.Headers.Add(binding.HeaderName, headerValue);
            return ScriptHttpResult.Succeeded();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            return Failed(
                ScriptHttpErrorCode.InvalidRequest,
                request.RequestUri,
                $"HTTP secret header '{binding.HeaderName}' is invalid: {exception.Message}");
        }
    }

    private static ScriptHttpError MapSecretError(Uri? uri, ScriptSecretError error) =>
        new(
            error.Code == ScriptSecretErrorCode.InvalidName
                ? ScriptHttpErrorCode.InvalidRequest
                : ScriptHttpErrorCode.BlockedByPolicy,
            uri,
            $"HTTP secret header binding failed for secret '{error.Name}': {error.Message}");

    private static ScriptHttpResult Failed(
        ScriptHttpErrorCode code,
        Uri? uri,
        string message) =>
        ScriptHttpResult.Failed(new ScriptHttpError(code, uri, message));
}
