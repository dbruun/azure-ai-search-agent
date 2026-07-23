using System.ComponentModel;
using System.Text.Json;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

namespace AiSearchAgent.Tools;

/// <summary>
/// Function tool that recreates the Foundry <c>azure_ai_search</c> tool for the
/// AI Search Agent. It runs a semantic query against the <c>documents-index</c>
/// (using the index's <c>sem</c> semantic configuration) and returns the top
/// matching chunks along with the source document and page range so the agent
/// can cite its sources.
/// </summary>
internal sealed class AzureAiSearchTool(SearchClient searchClient, int topK = 11, string semanticConfigurationName = AzureAiSearchTool.DefaultSemanticConfigurationName)
{
    /// <summary>Name of the semantic configuration defined on <c>documents-index</c>.</summary>
    internal const string DefaultSemanticConfigurationName = "sem";

    private readonly SearchClient _searchClient = searchClient;
    private readonly int _topK = topK;
    private readonly string _semanticConfigurationName = semanticConfigurationName;

    [Description(
        "Search the documents-index Azure AI Search index for passages relevant to the user's question. " +
        "Returns the most relevant text chunks together with their source document name and page range. " +
        "Always use the returned sourceDoc, pageFrom and pageTo values to cite pages; never invent sources.")]
    public async Task<string> SearchAsync(
        [Description("The natural-language search query derived from the user's question.")] string query,
        CancellationToken cancellationToken = default)
    {
        SearchOptions options = BuildSearchOptions(_topK, _semanticConfigurationName);

        SearchResults<SearchDocument> results =
            await _searchClient.SearchAsync<SearchDocument>(query, options, cancellationToken);

        var hits = new List<(SearchDocument Document, double? Score)>();
        await foreach (SearchResult<SearchDocument> result in results.GetResultsAsync())
        {
            hits.Add((result.Document, result.Score));
        }

        return SerializeResults(hits);
    }

    /// <summary>
    /// Builds the <see cref="SearchOptions"/> for a semantic query (top_k), using the
    /// index's <c>sem</c> semantic configuration and requesting only the fields needed
    /// for answers and citations.
    /// </summary>
    internal static SearchOptions BuildSearchOptions(int topK, string semanticConfigurationName = DefaultSemanticConfigurationName)
    {
        var options = new SearchOptions
        {
            // Semantic ranking over the documents-index 'sem' configuration, top_k = 11.
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = semanticConfigurationName,
            },
            Size = topK,
        };

        // Only request the retrievable fields we need for answers and citations.
        options.Select.Add("id");
        options.Select.Add("chunk");
        options.Select.Add("sourceDoc");
        options.Select.Add("title");
        options.Select.Add("url");
        options.Select.Add("pageFrom");
        options.Select.Add("pageTo");

        return options;
    }

    /// <summary>
    /// Projects search hits into the JSON payload returned to the agent, preserving
    /// the source document and page metadata used for citations.
    /// </summary>
    internal static string SerializeResults(IReadOnlyCollection<(SearchDocument Document, double? Score)> hits)
    {
        var projected = new List<object>(hits.Count);
        foreach ((SearchDocument doc, double? score) in hits)
        {
            projected.Add(new
            {
                score,
                sourceDoc = GetString(doc, "sourceDoc"),
                title = GetString(doc, "title"),
                url = GetString(doc, "url"),
                pageFrom = GetInt(doc, "pageFrom"),
                pageTo = GetInt(doc, "pageTo"),
                chunk = GetString(doc, "chunk"),
            });
        }

        return JsonSerializer.Serialize(new { count = projected.Count, results = projected });
    }

    internal static string? GetString(SearchDocument doc, string field) =>
        doc.TryGetValue(field, out object? value) ? value?.ToString() : null;

    internal static int? GetInt(SearchDocument doc, string field) =>
        doc.TryGetValue(field, out object? value) && value is not null && int.TryParse(value.ToString(), out int i)
            ? i
            : null;
}
