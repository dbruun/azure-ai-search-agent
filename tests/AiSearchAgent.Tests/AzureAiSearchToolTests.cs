using System.Text.Json;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AiSearchAgent.Tools;
using Xunit;

namespace AiSearchAgent.Tests;

/// <summary>
/// Tests for <see cref="AzureAiSearchTool"/>, focusing on the fidelity-critical
/// behavior (query_type = semantic using the 'sem' configuration, top_k, selected
/// fields) and the citation payload shape.
/// </summary>
public class AzureAiSearchToolTests
{
    [Fact]
    public void BuildSearchOptions_UsesSemanticQueryType()
    {
        SearchOptions options = AzureAiSearchTool.BuildSearchOptions(topK: 11);

        Assert.Equal(SearchQueryType.Semantic, options.QueryType);
    }

    [Fact]
    public void BuildSearchOptions_UsesSemConfiguration()
    {
        SearchOptions options = AzureAiSearchTool.BuildSearchOptions(topK: 11);

        Assert.NotNull(options.SemanticSearch);
        Assert.Equal("sem", options.SemanticSearch!.SemanticConfigurationName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(50)]
    public void BuildSearchOptions_SetsTopKAsSize(int topK)
    {
        SearchOptions options = AzureAiSearchTool.BuildSearchOptions(topK);

        Assert.Equal(topK, options.Size);
    }

    [Fact]
    public void BuildSearchOptions_SelectsCitationFields()
    {
        SearchOptions options = AzureAiSearchTool.BuildSearchOptions(topK: 11);

        Assert.Equal(
            new[] { "id", "chunk", "sourceDoc", "title", "url", "pageFrom", "pageTo" },
            options.Select.ToArray());
    }

    [Fact]
    public void SerializeResults_EmptyHits_ReturnsZeroCount()
    {
        string json = AzureAiSearchTool.SerializeResults(
            Array.Empty<(SearchDocument, double?)>());

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Empty(doc.RootElement.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public void SerializeResults_ProjectsCitationMetadata()
    {
        var document = new SearchDocument(new Dictionary<string, object>
        {
            ["id"] = "chunk-1",
            ["chunk"] = "The reactor operates at design pressure.",
            ["sourceDoc"] = "DesignManual.pdf",
            ["title"] = "Design Manual",
            ["url"] = "https://example/doc",
            ["pageFrom"] = 7,
            ["pageTo"] = 8,
        });

        string json = AzureAiSearchTool.SerializeResults(
            new (SearchDocument, double?)[] { (document, 0.87) });

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());

        JsonElement hit = doc.RootElement.GetProperty("results")[0];
        Assert.Equal("DesignManual.pdf", hit.GetProperty("sourceDoc").GetString());
        Assert.Equal("Design Manual", hit.GetProperty("title").GetString());
        Assert.Equal("https://example/doc", hit.GetProperty("url").GetString());
        Assert.Equal(7, hit.GetProperty("pageFrom").GetInt32());
        Assert.Equal(8, hit.GetProperty("pageTo").GetInt32());
        Assert.Equal("The reactor operates at design pressure.", hit.GetProperty("chunk").GetString());
        Assert.Equal(0.87, hit.GetProperty("score").GetDouble(), 3);
    }

    [Fact]
    public void SerializeResults_MissingFields_AreNull()
    {
        var document = new SearchDocument(new Dictionary<string, object>
        {
            ["id"] = "chunk-2",
            ["chunk"] = "Some text without page metadata.",
        });

        string json = AzureAiSearchTool.SerializeResults(
            new (SearchDocument, double?)[] { (document, null) });

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement hit = doc.RootElement.GetProperty("results")[0];

        Assert.Equal(JsonValueKind.Null, hit.GetProperty("sourceDoc").ValueKind);
        Assert.Equal(JsonValueKind.Null, hit.GetProperty("pageFrom").ValueKind);
        Assert.Equal(JsonValueKind.Null, hit.GetProperty("pageTo").ValueKind);
        Assert.Equal(JsonValueKind.Null, hit.GetProperty("score").ValueKind);
    }

    [Fact]
    public void SerializeResults_PreservesOrderOfHits()
    {
        var first = new SearchDocument(new Dictionary<string, object> { ["sourceDoc"] = "A.pdf" });
        var second = new SearchDocument(new Dictionary<string, object> { ["sourceDoc"] = "B.pdf" });

        string json = AzureAiSearchTool.SerializeResults(
            new (SearchDocument, double?)[] { (first, 0.9), (second, 0.5) });

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement results = doc.RootElement.GetProperty("results");
        Assert.Equal("A.pdf", results[0].GetProperty("sourceDoc").GetString());
        Assert.Equal("B.pdf", results[1].GetProperty("sourceDoc").GetString());
    }

    [Fact]
    public void GetString_MissingField_ReturnsNull()
    {
        var document = new SearchDocument(new Dictionary<string, object> { ["id"] = "x" });

        Assert.Null(AzureAiSearchTool.GetString(document, "sourceDoc"));
    }

    [Fact]
    public void GetInt_NonNumericValue_ReturnsNull()
    {
        var document = new SearchDocument(new Dictionary<string, object> { ["pageFrom"] = "not-a-number" });

        Assert.Null(AzureAiSearchTool.GetInt(document, "pageFrom"));
    }

    [Fact]
    public void GetInt_NumericStringValue_IsParsed()
    {
        var document = new SearchDocument(new Dictionary<string, object> { ["pageFrom"] = "42" });

        Assert.Equal(42, AzureAiSearchTool.GetInt(document, "pageFrom"));
    }
}
