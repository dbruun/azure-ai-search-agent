using Azure.Core;
using Azure.Core.Pipeline;

namespace AiSearchAgent.Tools;

/// <summary>
/// HTTP pipeline policy that enables query-time document-level ACL enforcement
/// (preview) for Azure AI Search.
///
/// It does two things on every request:
/// <list type="number">
///   <item>Adds the <c>x-ms-query-source-authorization</c> header carrying the
///   calling user's Microsoft Entra token. The search service builds an internal
///   security filter from the token's <c>oid</c> and group memberships and drops
///   documents whose <c>UserIds</c>/<c>GroupIds</c>/<c>RbacScope</c> permission
///   fields don't grant access.</item>
///   <item>Pins the request to a preview <c>api-version</c>, which is required for
///   permission-filter enforcement to take effect.</item>
/// </list>
///
/// The token is acquired for the <c>https://search.azure.com/.default</c> scope
/// using the supplied credential (typically the signed-in user), so the identity
/// evaluated at query time is the end user, not the service.
/// </summary>
internal sealed class QueryAclPolicy(TokenCredential credential, string apiVersion = QueryAclPolicy.DefaultPreviewApiVersion)
    : HttpPipelinePolicy
{
    /// <summary>Preview API version that supports permission-filter enforcement and SharePoint groups.</summary>
    internal const string DefaultPreviewApiVersion = "2026-05-01-preview";

    private const string QuerySourceAuthorizationHeader = "x-ms-query-source-authorization";
    private static readonly string[] Scopes = ["https://search.azure.com/.default"];

    private readonly TokenCredential _credential = credential;
    private readonly string _apiVersion = apiVersion;

    public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        await ApplyAsync(message, async: true).ConfigureAwait(false);
        await ProcessNextAsync(message, pipeline).ConfigureAwait(false);
    }

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        ApplyAsync(message, async: false).AsTask().GetAwaiter().GetResult();
        ProcessNext(message, pipeline);
    }

    private async ValueTask ApplyAsync(HttpMessage message, bool async)
    {
        OverrideApiVersion(message);

        AccessToken token = async
            ? await _credential.GetTokenAsync(new TokenRequestContext(Scopes), message.CancellationToken).ConfigureAwait(false)
            : _credential.GetToken(new TokenRequestContext(Scopes), message.CancellationToken);

        message.Request.Headers.SetValue(QuerySourceAuthorizationHeader, token.Token);
    }

    private void OverrideApiVersion(HttpMessage message)
    {
        // Replace the SDK's stable api-version with the preview version required
        // for permission-filter enforcement, preserving all other query parameters.
        Uri uri = message.Request.Uri.ToUri();
        if (string.IsNullOrEmpty(uri.Query))
        {
            return;
        }

        string[] pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        bool replaced = false;
        for (int i = 0; i < pairs.Length; i++)
        {
            if (pairs[i].StartsWith("api-version=", StringComparison.OrdinalIgnoreCase))
            {
                pairs[i] = "api-version=" + _apiVersion;
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            return;
        }

        var builder = new RequestUriBuilder();
        builder.Reset(uri);
        builder.Query = "?" + string.Join('&', pairs);
        message.Request.Uri.Reset(builder.ToUri());
    }
}
