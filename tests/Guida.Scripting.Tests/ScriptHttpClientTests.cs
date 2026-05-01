using System.Net;
using System.Net.Http;
using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptHttpClientTests
{
    [Fact]
    public void Http_client_can_be_registered_and_retrieved_from_host_context()
    {
        var client = new FakeScriptHttpClient();
        var context = ScriptHostContext.Empty.WithCapability<IScriptHttpClient>(client);

        Assert.True(context.TryGetCapability<IScriptHttpClient>(out var found));
        Assert.Same(client, found);
        Assert.Same(client, context.GetCapability<IScriptHttpClient>());
    }

    [Fact]
    public void Missing_http_client_uses_capability_unavailable_reporting()
    {
        var unavailable = ScriptCapabilityUnavailable.For<IScriptHttpClient>();
        var result = unavailable.ToExecutionResult();

        Assert.False(ScriptHostContext.Empty.TryGetCapability<IScriptHttpClient>(out _));
        Assert.Equal(typeof(IScriptHttpClient).FullName, unavailable.CapabilityName);
        Assert.False(result.Success);
        Assert.Contains(nameof(IScriptHttpClient), result.Error);
    }

    [Fact]
    public void Bcl_http_request_defaults_to_get()
    {
        using var request = new HttpRequestMessage();

        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task Default_policy_accepts_absolute_https_requests()
    {
        var policy = new ScriptHttpPolicy();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");

        var result = await policy.ValidateRequestAsync(request);

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("http://example.test/")]
    [InlineData("file:///tmp/file.txt")]
    public async Task Default_policy_rejects_disallowed_schemes(string uri)
    {
        var policy = new ScriptHttpPolicy();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        var result = await policy.ValidateRequestAsync(request);

        Assert.False(result.Success);
        Assert.Equal(ScriptHttpErrorCode.BlockedByPolicy, result.Error?.Code);
    }

    [Fact]
    public async Task Policy_rejects_relative_or_null_request_uris()
    {
        var policy = new ScriptHttpPolicy();
        using var relative = new HttpRequestMessage(HttpMethod.Get, "/relative");
        using var missing = new HttpRequestMessage();

        var relativeResult = await policy.ValidateRequestAsync(relative);
        var missingResult = await policy.ValidateRequestAsync(missing);

        Assert.False(relativeResult.Success);
        Assert.Equal(ScriptHttpErrorCode.InvalidRequest, relativeResult.Error?.Code);
        Assert.False(missingResult.Success);
        Assert.Equal(ScriptHttpErrorCode.InvalidRequest, missingResult.Error?.Code);
    }

    [Fact]
    public async Task Policy_rejects_oversized_request_body_when_content_length_is_known()
    {
        var policy = new ScriptHttpPolicy { MaxRequestBodyBytes = 4 };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/")
        {
            Content = new ByteArrayContent([1, 2, 3, 4, 5])
        };

        var result = await policy.ValidateRequestAsync(request);

        Assert.False(result.Success);
        Assert.Equal(ScriptHttpErrorCode.BlockedByPolicy, result.Error?.Code);
    }

    [Fact]
    public async Task Policy_honors_explicitly_allowed_schemes()
    {
        var policy = new ScriptHttpPolicy { AllowedSchemes = ["https", "http"] };
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/");

        var result = await policy.ValidateRequestAsync(request);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Policy_rejects_timeout_above_configured_maximum()
    {
        var policy = new ScriptHttpPolicy { MaxTimeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        request.Options.Set(ScriptHttpPolicy.TimeoutOption, TimeSpan.FromSeconds(10));

        var result = await policy.ValidateRequestAsync(request);

        Assert.False(result.Success);
        Assert.Equal(ScriptHttpErrorCode.BlockedByPolicy, result.Error?.Code);
    }

    [Fact]
    public void Http_result_helpers_model_success_and_failure()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var error = new ScriptHttpError(
            ScriptHttpErrorCode.NetworkError,
            new Uri("https://example.test/"),
            "network failed");

        var success = ScriptHttpResult<HttpResponseMessage>.Succeeded(response);
        var failure = ScriptHttpResult<HttpResponseMessage>.Failed(error);
        var emptySuccess = ScriptHttpResult.Succeeded();
        var emptyFailure = ScriptHttpResult.Failed(error);

        Assert.True(success.Success);
        Assert.Same(response, success.Value);
        Assert.Null(success.Error);
        Assert.False(failure.Success);
        Assert.Null(failure.Value);
        Assert.Same(error, failure.Error);
        Assert.True(emptySuccess.Success);
        Assert.False(emptyFailure.Success);
        Assert.Same(error, emptyFailure.Error);
    }

    [Fact]
    public void Http_error_converts_to_failed_execution_result()
    {
        var error = new ScriptHttpError(
            ScriptHttpErrorCode.BlockedByPolicy,
            new Uri("https://example.test/"),
            "blocked");

        var result = error.ToExecutionResult();

        Assert.False(result.Success);
        Assert.Equal("blocked", result.Error);
    }

    [Fact]
    public async Task Policy_client_sends_valid_requests_to_inner_handler()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                RequestMessage = request,
                ReasonPhrase = "Accepted",
                Content = new ByteArrayContent([1, 2, 3])
            };
            response.Headers.Add("X-Test", "ok");
            return Task.FromResult(response);
        });
        var client = new PolicyScriptHttpClient(new HttpMessageInvoker(handler));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/path");

        var result = await client.SendAsync(request);

        Assert.True(result.Success);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpStatusCode.Accepted, result.Value?.StatusCode);
        Assert.Equal("Accepted", result.Value?.ReasonPhrase);
        Assert.Equal("ok", Assert.Single(result.Value!.Headers.GetValues("X-Test")));
        Assert.Equal([1, 2, 3], await result.Value.Content.ReadAsByteArrayAsync());
        Assert.Equal(new Uri("https://example.test/path"), result.Value.RequestMessage?.RequestUri);

        result.Value.Dispose();
    }

    [Fact]
    public async Task Policy_client_maps_known_oversized_response_to_response_too_large()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4, 5])
            }));
        var client = new PolicyScriptHttpClient(
            new HttpMessageInvoker(handler),
            new ScriptHttpPolicy { MaxResponseBodyBytes = 4 });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");

        var result = await client.SendAsync(request);

        Assert.False(result.Success);
        Assert.Equal(ScriptHttpErrorCode.ResponseTooLarge, result.Error?.Code);
    }

    [Fact]
    public async Task Policy_client_maps_http_request_exception_to_network_error()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new HttpRequestException("network down"));
        var client = new PolicyScriptHttpClient(new HttpMessageInvoker(handler));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");

        var result = await client.SendAsync(request);

        Assert.False(result.Success);
        Assert.Equal(ScriptHttpErrorCode.NetworkError, result.Error?.Code);
        Assert.Equal("network down", result.Error?.Message);
    }

    [Fact]
    public async Task Policy_client_maps_policy_timeout_to_timeout_error()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new PolicyScriptHttpClient(
            new HttpMessageInvoker(handler),
            new ScriptHttpPolicy { MaxTimeout = TimeSpan.FromMilliseconds(10) });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");

        var result = await client.SendAsync(request);

        Assert.False(result.Success);
        Assert.Equal(ScriptHttpErrorCode.Timeout, result.Error?.Code);
    }

    [Fact]
    public async Task Policy_client_propagates_caller_cancellation()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new PolicyScriptHttpClient(new HttpMessageInvoker(handler));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.SendAsync(request, cancellationTokenSource.Token));
    }

    private sealed class FakeScriptHttpClient : IScriptHttpClient
    {
        public Task<ScriptHttpResult<HttpResponseMessage>> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(ScriptHttpResult<HttpResponseMessage>.Succeeded(
                new HttpResponseMessage(HttpStatusCode.OK)));
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            : this((request, _) => handler(request))
        {
        }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return _handler(request, cancellationToken);
        }
    }
}
