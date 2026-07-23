using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Moq;
using AiSearchAgent.Tools;
using Xunit;

namespace AiSearchAgent.Tests;
/// <summary>
/// Exercises <see cref="AzureAiSearchTool.SearchAsync"/> end-to-end against a
/// mocked <see cref="SearchClient"/>, verifying the query and options passed to
/// the service and the serialized citation payload returned to the agent.
/// </summary>
public class AzureAiSearchToolSearchAsyncTests
{
    [Fact]
    public async Task SearchAsync_PassesQueryAndSemanticOptionsToClient()
    {
        SearchOptions? captured = null;
        string? capturedQuery = null;

        var client = new Mock<SearchClient>();
        client
            .Setup(c => c.SearchAsync<SearchDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>((q, o, _) =>
            {
                capturedQuery = q;
                captured = o;
            })
            .ReturnsAsync(BuildResponse());

        var tool = new AzureAiSearchTool(client.Object, topK: 11);

        await tool.SearchAsync("reactor design pressure");

        Assert.Equal("reactor design pressure", capturedQuery);
        Assert.NotNull(captured);
        Assert.Equal(SearchQueryType.Semantic, captured!.QueryType);
        Assert.Equal("sem", captured.SemanticSearch?.SemanticConfigurationName);
        Assert.Equal(11, captured.Size);
    }

    [Fact]
    public async Task SearchAsync_ReturnsSerializedHitsWithCitations()
    {
        var document = new SearchDocument(new Dictionary<string, object>
        {
            ["sourceDoc"] = "HandbookA.pdf",
            ["pageFrom"] = 30,
            ["pageTo"] = 31,
            ["chunk"] = "Relevant passage.",
        });

        var client = new Mock<SearchClient>();
        client
            .Setup(c => c.SearchAsync<SearchDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildResponse((document, 0.75)));

        var tool = new AzureAiSearchTool(client.Object, topK: 11);

        string json = await tool.SearchAsync("query");

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        var hit = doc.RootElement.GetProperty("results")[0];
        Assert.Equal("HandbookA.pdf", hit.GetProperty("sourceDoc").GetString());
        Assert.Equal(30, hit.GetProperty("pageFrom").GetInt32());
        Assert.Equal(31, hit.GetProperty("pageTo").GetInt32());
    }

    private static Response<SearchResults<SearchDocument>> BuildResponse(
        params (SearchDocument Document, double? Score)[] hits)
    {
        var results = hits
            .Select(h => SearchModelFactory.SearchResult<SearchDocument>(h.Document, h.Score, highlights: null))
            .ToList();

        SearchResults<SearchDocument> searchResults = SearchModelFactory.SearchResults(
            results,
            totalCount: hits.Length,
            facets: null,
            coverage: null,
            rawResponse: Mock.Of<Response>());

        return Response.FromValue(searchResults, Mock.Of<Response>());
    }
}
