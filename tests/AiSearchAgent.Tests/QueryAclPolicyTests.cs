using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using AiSearchAgent.Tools;
using Xunit;

namespace AiSearchAgent.Tests;

/// <summary>
/// Verifies that <see cref="QueryAclPolicy"/> enables query-time document-level
/// ACL enforcement by (1) attaching the caller's token as the
/// <c>x-ms-query-source-authorization</c> header and (2) pinning the request to
/// the preview api-version required for permission-filter enforcement.
/// </summary>
public class QueryAclPolicyTests
{
    [Fact]
    public async Task Policy_AddsQuerySourceAuthorizationHeaderAndOverridesApiVersion()
    {
        var credential = new StubCredential("stub-user-token");
        var policy = new QueryAclPolicy(credential);

        HttpMessage message = await SendThroughPolicyAsync(
            policy,
            "https://svc.search.windows.net/indexes/sharepoint-index/docs/search.post.search?api-version=2024-07-01");

        Assert.True(message.Request.Headers.TryGetValue("x-ms-query-source-authorization", out string? headerValue));
        Assert.Equal("stub-user-token", headerValue);

        Assert.Contains("api-version=2026-05-01-preview", message.Request.Uri.ToUri().Query);
        Assert.DoesNotContain("2024-07-01", message.Request.Uri.ToUri().Query);
    }

    [Fact]
    public async Task Policy_PreservesOtherQueryParameters()
    {
        var policy = new QueryAclPolicy(new StubCredential("t"), apiVersion: "2026-05-01-preview");

        HttpMessage message = await SendThroughPolicyAsync(
            policy,
            "https://svc.search.windows.net/indexes/i/docs?api-version=2024-07-01&$top=5");

        string query = message.Request.Uri.ToUri().Query;
        Assert.Contains("$top=5", query);
        Assert.Contains("api-version=2026-05-01-preview", query);
    }

    [Fact]
    public async Task Policy_UsesConfiguredApiVersion()
    {
        var policy = new QueryAclPolicy(new StubCredential("t"), apiVersion: "2099-01-01-preview");

        HttpMessage message = await SendThroughPolicyAsync(
            policy,
            "https://svc.search.windows.net/indexes/i/docs?api-version=2024-07-01");

        Assert.Contains("api-version=2099-01-01-preview", message.Request.Uri.ToUri().Query);
    }

    /// <summary>
    /// Runs a single POST request through the policy using a real transport
    /// request object and a terminal policy that short-circuits the network call.
    /// </summary>
    private static async Task<HttpMessage> SendThroughPolicyAsync(QueryAclPolicy policy, string uri)
    {
        using var transport = new HttpClientTransport();
        Request request = transport.CreateRequest();
        request.Method = RequestMethod.Post;
        request.Uri.Reset(new Uri(uri));

        var message = new HttpMessage(request, new ResponseClassifier());

        var pipeline = new HttpPipelinePolicy[] { new TerminalPolicy() };
        await policy.ProcessAsync(message, pipeline);

        return message;
    }

    /// <summary>Terminal policy that stops the chain without performing I/O.</summary>
    private sealed class TerminalPolicy : HttpPipelinePolicy
    {
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline) { }

        public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
            => ValueTask.CompletedTask;
    }

    private sealed class StubCredential(string token) : TokenCredential
    {
        private readonly string _token = token;

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(_token, DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1)));
    }
}
