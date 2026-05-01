using System.Net.Http;

namespace Guida.Scripting;

/// <summary>
/// Host policy used to validate script HTTP requests.
/// </summary>
public sealed record ScriptHttpPolicy
{
    private static readonly IReadOnlyCollection<string> DefaultSchemes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "https" };

    /// <summary>
    /// Request option key used for per-request timeout requests.
    /// </summary>
    public static HttpRequestOptionsKey<TimeSpan> TimeoutOption { get; } =
        new("Guida.Scripting.Http.Timeout");

    /// <summary>
    /// URI schemes allowed by this policy.
    /// </summary>
    public IReadOnlyCollection<string> AllowedSchemes { get; init; } = DefaultSchemes;

    /// <summary>
    /// Optional maximum request body size when content length is known.
    /// </summary>
    public long? MaxRequestBodyBytes { get; init; }

    /// <summary>
    /// Optional maximum response body size when content length is known.
    /// </summary>
    public long? MaxResponseBodyBytes { get; init; }

    /// <summary>
    /// Optional maximum request timeout.
    /// </summary>
    public TimeSpan? MaxTimeout { get; init; }

    /// <summary>
    /// Validates a request against the host policy.
    /// </summary>
    public Task<ScriptHttpResult> ValidateRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request is null)
        {
            return Task.FromResult(Failed(
                ScriptHttpErrorCode.InvalidRequest,
                null,
                "HTTP request cannot be null."));
        }

        if (request.RequestUri is null || !request.RequestUri.IsAbsoluteUri)
        {
            return Task.FromResult(Failed(
                ScriptHttpErrorCode.InvalidRequest,
                request.RequestUri,
                "HTTP request URI must be absolute."));
        }

        if (!IsSchemeAllowed(request.RequestUri.Scheme))
        {
            return Task.FromResult(Failed(
                ScriptHttpErrorCode.BlockedByPolicy,
                request.RequestUri,
                $"HTTP scheme '{request.RequestUri.Scheme}' is not allowed."));
        }

        if (MaxRequestBodyBytes is < 0)
        {
            return Task.FromResult(Failed(
                ScriptHttpErrorCode.InvalidRequest,
                request.RequestUri,
                "Max request body bytes cannot be negative."));
        }

        if (request.Content?.Headers.ContentLength is { } requestLength &&
            MaxRequestBodyBytes is { } maxRequestBodyBytes &&
            requestLength > maxRequestBodyBytes)
        {
            return Task.FromResult(Failed(
                ScriptHttpErrorCode.BlockedByPolicy,
                request.RequestUri,
                $"HTTP request body exceeds the configured limit of {maxRequestBodyBytes} bytes."));
        }

        if (request.Options.TryGetValue(TimeoutOption, out var requestTimeout))
        {
            if (requestTimeout <= TimeSpan.Zero)
            {
                return Task.FromResult(Failed(
                    ScriptHttpErrorCode.InvalidRequest,
                    request.RequestUri,
                    "HTTP request timeout must be greater than zero."));
            }

            if (MaxTimeout is { } maxTimeout && requestTimeout > maxTimeout)
            {
                return Task.FromResult(Failed(
                    ScriptHttpErrorCode.BlockedByPolicy,
                    request.RequestUri,
                    $"HTTP request timeout exceeds the configured limit of {maxTimeout}."));
            }
        }

        if (MaxTimeout is { } policyTimeout && policyTimeout <= TimeSpan.Zero)
        {
            return Task.FromResult(Failed(
                ScriptHttpErrorCode.InvalidRequest,
                request.RequestUri,
                "Max HTTP timeout must be greater than zero."));
        }

        return Task.FromResult(ScriptHttpResult.Succeeded());
    }

    internal TimeSpan? GetEffectiveTimeout(HttpRequestMessage request) =>
        request.Options.TryGetValue(TimeoutOption, out var requestTimeout)
            ? requestTimeout
            : MaxTimeout;

    private bool IsSchemeAllowed(string scheme) =>
        AllowedSchemes?.Any(allowedScheme =>
            string.Equals(allowedScheme, scheme, StringComparison.OrdinalIgnoreCase)) == true;

    private static ScriptHttpResult Failed(
        ScriptHttpErrorCode code,
        Uri? uri,
        string message) =>
        ScriptHttpResult.Failed(new ScriptHttpError(code, uri, message));
}
