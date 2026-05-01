using System.Net;
using System.Net.Http;
using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptSecretTests
{
    [Fact]
    public void Secret_provider_can_be_registered_and_retrieved_from_host_context()
    {
        var provider = new ScriptInMemorySecretProvider(new Dictionary<string, string>());
        var context = ScriptHostContext.Empty.WithCapability<IScriptSecretProvider>(provider);

        Assert.True(context.TryGetCapability<IScriptSecretProvider>(out var found));
        Assert.Same(provider, found);
        Assert.Same(provider, context.GetCapability<IScriptSecretProvider>());
    }

    [Fact]
    public void Missing_secret_provider_uses_capability_unavailable_reporting()
    {
        var unavailable = ScriptCapabilityUnavailable.For<IScriptSecretProvider>();
        var result = unavailable.ToExecutionResult();

        Assert.False(ScriptHostContext.Empty.TryGetCapability<IScriptSecretProvider>(out _));
        Assert.Equal(typeof(IScriptSecretProvider).FullName, unavailable.CapabilityName);
        Assert.False(result.Success);
        Assert.Contains(nameof(IScriptSecretProvider), result.Error);
    }

    [Fact]
    public async Task In_memory_provider_returns_configured_secret()
    {
        var provider = new ScriptInMemorySecretProvider(
            new Dictionary<string, string> { ["api-token"] = "secret-value" });

        var result = await provider.GetSecretAsync(new ScriptSecretReference("api-token"));

        Assert.True(result.Success);
        Assert.Equal("api-token", result.Value?.Name);
        Assert.Equal("secret-value", result.Value?.Value);
    }

    [Fact]
    public async Task In_memory_provider_uses_exact_case_sensitive_names()
    {
        var provider = new ScriptInMemorySecretProvider(
            new Dictionary<string, string> { ["ApiToken"] = "secret-value" });

        var result = await provider.GetSecretAsync(new ScriptSecretReference("apitoken"));

        Assert.False(result.Success);
        Assert.Equal(ScriptSecretErrorCode.NotFound, result.Error?.Code);
    }

    [Fact]
    public async Task In_memory_provider_returns_not_found_for_missing_secret()
    {
        var provider = new ScriptInMemorySecretProvider(new Dictionary<string, string>());

        var result = await provider.GetSecretAsync(new ScriptSecretReference("missing"));

        Assert.False(result.Success);
        Assert.Equal(ScriptSecretErrorCode.NotFound, result.Error?.Code);
        Assert.Equal("missing", result.Error?.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task In_memory_provider_returns_invalid_name_for_empty_secret_names(string name)
    {
        var provider = new ScriptInMemorySecretProvider(new Dictionary<string, string>());

        var result = await provider.GetSecretAsync(new ScriptSecretReference(name));

        Assert.False(result.Success);
        Assert.Equal(ScriptSecretErrorCode.InvalidName, result.Error?.Code);
    }

    [Fact]
    public void Secret_error_converts_to_failed_execution_result()
    {
        var error = new ScriptSecretError(
            ScriptSecretErrorCode.AccessDenied,
            "api-token",
            "Secret access was denied.");

        var result = error.ToExecutionResult();

        Assert.False(result.Success);
        Assert.Equal(error.Message, result.Error);
    }

    [Fact]
    public void Header_binding_helpers_attach_and_retrieve_bindings()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        var binding = new ScriptHttpSecretHeaderBinding
        {
            HeaderName = "Authorization",
            Secret = new ScriptSecretReference("api-token"),
            ValuePrefix = "Bearer "
        };

        ScriptHttpSecretHeaderBindings.Set(request, [binding]);

        var retrieved = Assert.Single(ScriptHttpSecretHeaderBindings.Get(request));
        Assert.Same(binding, retrieved);
    }

    [Fact]
    public async Task Secret_binding_client_injects_bound_header_before_inner_send()
    {
        var provider = new ScriptInMemorySecretProvider(
            new Dictionary<string, string> { ["api-token"] = "secret-value" });
        var inner = new CapturingHttpClient();
        var client = new SecretBindingScriptHttpClient(inner, provider);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        ScriptHttpSecretHeaderBindings.Set(request,
        [
            new ScriptHttpSecretHeaderBinding
            {
                HeaderName = "X-Api-Key",
                Secret = new ScriptSecretReference("api-token")
            }
        ]);

        using var response = (await client.SendAsync(request)).Value;

        Assert.Single(inner.Requests);
        Assert.Equal("secret-value", Assert.Single(request.Headers.GetValues("X-Api-Key")));
    }

    [Fact]
    public async Task Secret_binding_client_applies_value_prefix()
    {
        var provider = new ScriptInMemorySecretProvider(
            new Dictionary<string, string> { ["api-token"] = "secret-value" });
        var inner = new CapturingHttpClient();
        var client = new SecretBindingScriptHttpClient(inner, provider);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        ScriptHttpSecretHeaderBindings.Set(request,
        [
            new ScriptHttpSecretHeaderBinding
            {
                HeaderName = "Authorization",
                Secret = new ScriptSecretReference("api-token"),
                ValuePrefix = "Bearer "
            }
        ]);

        using var response = (await client.SendAsync(request)).Value;

        Assert.Equal("Bearer secret-value", Assert.Single(request.Headers.GetValues("Authorization")));
    }

    [Fact]
    public async Task Secret_binding_client_preserves_existing_header_when_replace_is_false()
    {
        var provider = new ThrowingSecretProvider();
        var inner = new CapturingHttpClient();
        var client = new SecretBindingScriptHttpClient(inner, provider);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        request.Headers.Add("Authorization", "Bearer existing");
        ScriptHttpSecretHeaderBindings.Set(request,
        [
            new ScriptHttpSecretHeaderBinding
            {
                HeaderName = "Authorization",
                Secret = new ScriptSecretReference("api-token")
            }
        ]);

        using var response = (await client.SendAsync(request)).Value;

        Assert.Equal("Bearer existing", Assert.Single(request.Headers.GetValues("Authorization")));
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Secret_binding_client_replaces_existing_header_when_requested()
    {
        var provider = new ScriptInMemorySecretProvider(
            new Dictionary<string, string> { ["api-token"] = "replacement" });
        var inner = new CapturingHttpClient();
        var client = new SecretBindingScriptHttpClient(inner, provider);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        request.Headers.Add("Authorization", "Bearer existing");
        ScriptHttpSecretHeaderBindings.Set(request,
        [
            new ScriptHttpSecretHeaderBinding
            {
                HeaderName = "Authorization",
                Secret = new ScriptSecretReference("api-token"),
                ValuePrefix = "Bearer ",
                ReplaceExisting = true
            }
        ]);

        using var response = (await client.SendAsync(request)).Value;

        Assert.Equal("Bearer replacement", Assert.Single(request.Headers.GetValues("Authorization")));
    }

    [Fact]
    public async Task Secret_binding_client_maps_secret_lookup_failure_to_http_failure()
    {
        var provider = new ScriptInMemorySecretProvider(
            new Dictionary<string, string> { ["api-token"] = "secret-value" });
        var inner = new CapturingHttpClient();
        var client = new SecretBindingScriptHttpClient(inner, provider);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        ScriptHttpSecretHeaderBindings.Set(request,
        [
            new ScriptHttpSecretHeaderBinding
            {
                HeaderName = "Authorization",
                Secret = new ScriptSecretReference("missing")
            }
        ]);

        var result = await client.SendAsync(request);

        Assert.False(result.Success);
        Assert.Equal(ScriptHttpErrorCode.BlockedByPolicy, result.Error?.Code);
        Assert.Empty(inner.Requests);
    }

    [Fact]
    public async Task Secret_binding_error_message_does_not_contain_secret_value()
    {
        var provider = new FailingSecretProvider("super-secret-token");
        var client = new SecretBindingScriptHttpClient(new CapturingHttpClient(), provider);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        ScriptHttpSecretHeaderBindings.Set(request,
        [
            new ScriptHttpSecretHeaderBinding
            {
                HeaderName = "Authorization",
                Secret = new ScriptSecretReference("api-token")
            }
        ]);

        var result = await client.SendAsync(request);

        Assert.False(result.Success);
        Assert.DoesNotContain("super-secret-token", result.Error?.Message);
    }

    [Fact]
    public async Task Secret_binding_client_maps_invalid_header_name_to_invalid_request()
    {
        var provider = new ScriptInMemorySecretProvider(
            new Dictionary<string, string> { ["api-token"] = "secret-value" });
        var client = new SecretBindingScriptHttpClient(new CapturingHttpClient(), provider);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        ScriptHttpSecretHeaderBindings.Set(request,
        [
            new ScriptHttpSecretHeaderBinding
            {
                HeaderName = "bad header",
                Secret = new ScriptSecretReference("api-token")
            }
        ]);

        var result = await client.SendAsync(request);

        Assert.False(result.Success);
        Assert.Equal(ScriptHttpErrorCode.InvalidRequest, result.Error?.Code);
    }

    [Fact]
    public async Task Secret_binding_client_propagates_cancellation_during_secret_lookup()
    {
        var provider = new CancelingSecretProvider();
        var client = new SecretBindingScriptHttpClient(new CapturingHttpClient(), provider);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        ScriptHttpSecretHeaderBindings.Set(request,
        [
            new ScriptHttpSecretHeaderBinding
            {
                HeaderName = "Authorization",
                Secret = new ScriptSecretReference("api-token")
            }
        ]);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.SendAsync(request, cancellationTokenSource.Token));
    }

    private sealed class CapturingHttpClient : IScriptHttpClient
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Task<ScriptHttpResult<HttpResponseMessage>> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            return Task.FromResult(ScriptHttpResult<HttpResponseMessage>.Succeeded(
                new HttpResponseMessage(HttpStatusCode.OK)));
        }
    }

    private sealed class ThrowingSecretProvider : IScriptSecretProvider
    {
        public Task<ScriptSecretResult<ScriptSecret>> GetSecretAsync(
            ScriptSecretReference reference,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Secret provider should not have been called.");
    }

    private sealed class FailingSecretProvider : IScriptSecretProvider
    {
        public FailingSecretProvider(string secretValue)
        {
        }

        public Task<ScriptSecretResult<ScriptSecret>> GetSecretAsync(
            ScriptSecretReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ScriptSecretResult<ScriptSecret>.Failed(
                new ScriptSecretError(
                    ScriptSecretErrorCode.AccessDenied,
                    reference.Name,
                    $"Secret access denied for '{reference.Name}'.")));
    }

    private sealed class CancelingSecretProvider : IScriptSecretProvider
    {
        public Task<ScriptSecretResult<ScriptSecret>> GetSecretAsync(
            ScriptSecretReference reference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(ScriptSecretResult<ScriptSecret>.Succeeded(new ScriptSecret()));
        }
    }
}
