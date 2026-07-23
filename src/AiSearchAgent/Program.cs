using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using AiSearchAgent;
using AiSearchAgent.Tools;

// -----------------------------------------------------------------------------
// Configuration
//   Values can come from appsettings.json, user-secrets, or environment vars.
//   Environment variables take precedence (e.g. AZURE_OPENAI__ENDPOINT).
// -----------------------------------------------------------------------------
IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets(typeof(AgentInstructions).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

string openAiEndpoint = Require(config, "AzureOpenAI:Endpoint", "AZURE_OPENAI_ENDPOINT");
string openAiDeployment = config["AzureOpenAI:Deployment"] ?? "gpt-4o";
string? openAiApiKey = config["AzureOpenAI:ApiKey"];

string searchEndpoint = Require(config, "AzureAISearch:Endpoint", "AZURE_SEARCH_ENDPOINT");
string searchIndex = config["AzureAISearch:IndexName"] ?? "documents-index";
string? searchApiKey = config["AzureAISearch:ApiKey"];
int topK = int.TryParse(config["AzureAISearch:TopK"], out int k) ? k : 11;

// SharePoint (ACL-enforced) index — optional second knowledge source.
string sharePointIndex = config["SharePointSearch:IndexName"] ?? "sharepoint-index";
bool sharePointEnabled = bool.TryParse(config["SharePointSearch:Enabled"], out bool spEnabled) && spEnabled;
int sharePointTopK = int.TryParse(config["SharePointSearch:TopK"], out int spk) ? spk : topK;

// Shared credential for Entra ID (RBAC) auth when keys are not provided.
var credential = new DefaultAzureCredential();

// -----------------------------------------------------------------------------
// Azure AI Search tool (recreates the Foundry azure_ai_search tool)
// -----------------------------------------------------------------------------
SearchClient searchClient = searchApiKey is { Length: > 0 }
    ? new SearchClient(new Uri(searchEndpoint), searchIndex, new AzureKeyCredential(searchApiKey))
    : new SearchClient(new Uri(searchEndpoint), searchIndex, credential);

var searchTool = new AzureAiSearchTool(searchClient, topK);

var tools = new List<Microsoft.Extensions.AI.AITool>
{
    AIFunctionFactory.Create(
        searchTool.SearchAsync,
        name: "azure_ai_search",
        description: "Search the documents-index Azure AI Search index and return relevant passages with source document and page metadata."),
};

// -----------------------------------------------------------------------------
// SharePoint ACL-enforced search tool (preview)
//   Query-time document-level access control: the signed-in user's Entra token
//   is forwarded via x-ms-query-source-authorization, so results are trimmed to
//   documents the user is permitted to see. Requires Entra (not key) auth.
// -----------------------------------------------------------------------------
if (sharePointEnabled)
{
    var aclOptions = new SearchClientOptions();
    aclOptions.AddPolicy(new QueryAclPolicy(credential), HttpPipelinePosition.PerCall);

    var sharePointClient = new SearchClient(new Uri(searchEndpoint), sharePointIndex, credential, aclOptions);
    var sharePointTool = new AzureAiSearchTool(sharePointClient, sharePointTopK);

    tools.Add(AIFunctionFactory.Create(
        sharePointTool.SearchAsync,
        name: "sharepoint_search",
        description: "Search the SharePoint-backed index. Results are automatically filtered to documents the current user is allowed to access (document-level ACL enforcement). Use for SharePoint content and cite sourceDoc and url."));
}

// -----------------------------------------------------------------------------
// Chat model (gpt-4o on Azure OpenAI) + Agent Framework agent
// -----------------------------------------------------------------------------
AzureOpenAIClient openAiClient = openAiApiKey is { Length: > 0 }
    ? new AzureOpenAIClient(new Uri(openAiEndpoint), new AzureKeyCredential(openAiApiKey))
    : new AzureOpenAIClient(new Uri(openAiEndpoint), credential);

IChatClient chatClient = openAiClient.GetChatClient(openAiDeployment).AsIChatClient();

AIAgent agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Name = "AI-Search-Agent",
    Instructions = AgentInstructions.Prompt,
    ChatOptions = new ChatOptions
    {
        Tools = tools,
    },
});

// -----------------------------------------------------------------------------
// Interactive chat loop
// -----------------------------------------------------------------------------
AgentThread thread = agent.GetNewThread();

string sources = sharePointEnabled ? $"{searchIndex} + {sharePointIndex} (ACL)" : searchIndex;
Console.WriteLine("AI Search Agent (gpt-4o + Azure AI Search: " + sources + ")");
Console.WriteLine("Type your question, or an empty line to exit.");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");
    string? input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input))
        break;

    Console.Write("Agent: ");
    await foreach (AgentRunResponseUpdate update in agent.RunStreamingAsync(input, thread))
    {
        Console.Write(update.Text);
    }
    Console.WriteLine();
    Console.WriteLine();
}

static string Require(IConfiguration config, string configKey, string envName)
{
    string? value = config[configKey] ?? Environment.GetEnvironmentVariable(envName);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Missing required configuration '{configKey}' (or environment variable '{envName}').");
    }
    return value;
}
