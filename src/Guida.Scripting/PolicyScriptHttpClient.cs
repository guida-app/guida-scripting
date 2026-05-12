using System.Net.Http;

namespace Guida.Scripting;

/// <summary>
/// Policy-aware HTTP capability wrapper around a .NET HTTP message invoker.
/// </summary>
public sealed class PolicyScriptHttpClient : IScriptHttpClient
{
    private readonly HttpMessageInvoker _inner;
    private readonly ScriptHttpPolicy _policy;

    /// <summary>
    /// Creates a policy-aware HTTP client.
    /// </summary>
    public PolicyScriptHttpClient(
        HttpMessageInvoker inner,
        ScriptHttpPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _policy = policy ?? new ScriptHttpPolicy();
    }

    /// <inheritdoc />
    public async Task<ScriptHttpResult<HttpResponseMessage>> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        var validation = await _policy
            .ValidateRequestAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.Success)
        {
            return ScriptHttpResult<HttpResponseMessage>.Failed(validation.Error!);
        }

        using var timeoutTokenSource = CreateTimeoutTokenSource(request, cancellationToken);
        var effectiveCancellationToken = timeoutTokenSource?.Token ?? cancellationToken;

        try
        {
            var response = await _inner
                .SendAsync(request, effectiveCancellationToken)
                .ConfigureAwait(false);

            if (response.Content?.Headers.ContentLength is { } responseLength &&
                _policy.MaxResponseBodyBytes is { } maxResponseBodyBytes &&
                responseLength > maxResponseBodyBytes)
            {
                response.Dispose();
                return ScriptHttpResult<HttpResponseMessage>.Failed(new ScriptHttpError(
                    ScriptHttpErrorCode.ResponseTooLarge,
                    request.RequestUri,
                    $"HTTP response body exceeds the configured limit of {maxResponseBodyBytes} bytes."));
            }

            return ScriptHttpResult<HttpResponseMessage>.Succeeded(response);
        }
        catch (HttpRequestException exception)
        {
            return ScriptHttpResult<HttpResponseMessage>.Failed(new ScriptHttpError(
                ScriptHttpErrorCode.NetworkError,
                request.RequestUri,
                exception.Message));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ScriptHttpResult<HttpResponseMessage>.Failed(new ScriptHttpError(
                ScriptHttpErrorCode.Timeout,
                request.RequestUri,
                "HTTP request timed out."));
        }
    }

    private CancellationTokenSource? CreateTimeoutTokenSource(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var timeout = _policy.GetEffectiveTimeout(request);
        if (timeout is null)
        {
            return null;
        }

        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationTokenSource.CancelAfter(timeout.Value);
        return cancellationTokenSource;
    }
}
