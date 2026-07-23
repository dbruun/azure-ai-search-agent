# AI Search Agent (.NET / Microsoft Agent Framework)

A code-first **agentic-framework** app that recreates a Microsoft Foundry
Azure AI Search agent using the **Microsoft Agent Framework** for .NET, plus
infrastructure to deploy the backing **Azure AI Search** service and index.

The agent answers questions grounded in the `documents-index` Azure AI Search index
and cites the source document and page numbers for every answer.

📖 **[Documentation site](https://dbruun.github.io/azure-ai-search-agent/)** — project overview, architecture, and setup (published to GitHub Pages from [`docs/`](docs/)).

## What this recreates

| Foundry agent setting | Value | Where it lives here |
| --- | --- | --- |
| Model | `gpt-4o` | `AzureOpenAI:Deployment` in `appsettings.json` |
| Instructions | full prompt | [AgentInstructions.cs](src/AiSearchAgent/AgentInstructions.cs) |
| Tool | `azure_ai_search` | [AzureAiSearchTool.cs](src/AiSearchAgent/Tools/AzureAiSearchTool.cs) |
| Index | `documents-index` | `AzureAISearch:IndexName` |
| Query type | `semantic` (`sem` config) | `SearchQueryType.Semantic` |
| Top K | `11` | `AzureAISearch:TopK` |

The backing search service `trsdemosearch` (Free SKU, Central US, free semantic
search) is reproduced in [infra/main.bicep](infra/main.bicep), and the index
schema is reproduced in [infra/documents-index.json](infra/documents-index.json).

## Project layout

```
src/AiSearchAgent/         Console app (Microsoft Agent Framework)
  Program.cs              Wires up gpt-4o + Azure AI Search tool(s), chat loop
  AgentInstructions.cs    Verbatim Foundry agent instructions
  Tools/AzureAiSearchTool.cs  Semantic search over an index with citations
  Tools/QueryAclPolicy.cs     Query-time document-level ACL enforcement (preview)
  appsettings.json        Endpoints / index / deployment settings
tests/AiSearchAgent.Tests/     xUnit tests (tool options, citation payload, ACL policy, instructions)
infra/
  main.bicep              Azure AI Search service (mirrors trsdemosearch)
  main.parameters.json    Deployment parameters
  documents-index.json    Exact documents-index schema (fields, semantic, vector)
  sharepoint-index.json   ACL-enabled index (permissionFilter fields)
  sharepoint-datasource.json  SharePoint data source (ACL ingestion)
  sharepoint-indexer.json     SharePoint indexer + ACL field mappings
scripts/
  create-index.ps1            Creates documents-index via the Search REST API
  create-sharepoint-index.ps1 Creates the SharePoint ACL pipeline (preview API)
```

## Two knowledge sources

| Index | Source | Access control |
| --- | --- | --- |
| `documents-index` | pushed documents | none (all results visible) |
| `sharepoint-index` | SharePoint Online (indexer) | **document-level ACLs**, enforced at query time |

The SharePoint index uses Azure AI Search **document-level access control (preview)**:
the indexer ingests each item's `UserIds`/`GroupIds` permission metadata, and at
query time the signed-in user's Microsoft Entra token is forwarded via the
`x-ms-query-source-authorization` header so results are trimmed to what the user
is allowed to see. See [SharePoint ACL setup](#4-optional-sharepoint-index-with-acls-preview).

## Prerequisites

- .NET 10 SDK
- An Azure OpenAI resource with a `gpt-4o` deployment
- Azure CLI (`az login`) for deployment
- An Azure AI Search service (deploy with the Bicep below, or reuse `trsdemosearch`)

## 1. Deploy Azure AI Search

```powershell
az group create --name TRSDemo --location centralus

az deployment group create `
  --resource-group TRSDemo `
  --template-file infra/main.bicep `
  --parameters infra/main.parameters.json
```

Then create the index (data-plane, not part of ARM/Bicep):

```powershell
./scripts/create-index.ps1 -SearchServiceName trsdemosearch -ResourceGroup TRSDemo
```

## 2. Configure the app

Set values in `src/AiSearchAgent/appsettings.json`, or use environment
variables / user-secrets. Environment variables take precedence:

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://<your-openai>.openai.azure.com/"
$env:AZURE_SEARCH_ENDPOINT = "https://trsdemosearch.search.windows.net"
```

### Authentication

By default the app uses `DefaultAzureCredential` (Entra ID / RBAC), so `az login`
is enough locally. For RBAC you need:

- **Cognitive Services OpenAI User** on the Azure OpenAI resource
- **Search Index Data Reader** on the Azure AI Search service

Alternatively, set `AzureOpenAI:ApiKey` and/or `AzureAISearch:ApiKey` to use keys.

## 3. Run

```powershell
dotnet run --project src/AiSearchAgent
```

Ask a question; the agent will search `documents-index` and answer with a
**Pages Referenced** section citing the source documents and page ranges.

## 4. (Optional) SharePoint index with ACLs (preview)

This adds a second index backed by SharePoint Online, with **document-level
access control** enforced at query time.

### Prerequisites

- An Entra app registration granted the SharePoint/Graph permissions required by
  the Azure AI Search SharePoint indexer (see Microsoft Learn:
  *"Index data from SharePoint Online"* and *"Use a SharePoint indexer to ingest
  permission metadata"*).
- The search service using **Entra ID auth** for queries (ACL enforcement relies
  on the caller's token; API keys bypass it).

### Provision

1. Edit [`infra/sharepoint-datasource.json`](infra/sharepoint-datasource.json) and
   replace the connection-string placeholders (site URL, `ApplicationId`, `TenantId`).
2. Create the index, data source, and indexer with the preview API:

   ```powershell
   ./scripts/create-sharepoint-index.ps1 `
     -SearchServiceName trsdemosearch `
     -ResourceGroup TRSDemo `
     -RunIndexer
   ```

### Enable in the app

```jsonc
// appsettings.json
"SharePointSearch": {
  "Enabled": true,
  "IndexName": "sharepoint-index",
  "TopK": 11
}
```

When enabled, the agent gains a `sharepoint_search` tool. Each query forwards the
signed-in user's Entra token via `x-ms-query-source-authorization` (and pins the
`2026-05-01-preview` API version), so results are automatically trimmed to
documents that user is permitted to access. The identity comes from
`DefaultAzureCredential`, so sign in as the end user (e.g. `az login` or an
interactive credential) — not a service principal.

### How ACLs flow end-to-end

| Stage | What happens |
| --- | --- |
| Index | `permissionFilterOption: enabled`; `UserIds`/`GroupIds`/`RbacScope` fields tagged with `permissionFilter` |
| Ingest | SharePoint data source `indexerPermissionOptions`; indexer maps `metadata_user_ids`/`metadata_group_ids` (and `metadata_sharepoint_site_url` for group resolution) |
| Query | user token in `x-ms-query-source-authorization`; the service builds an internal security filter and drops unauthorized docs |

## Tests

```powershell
dotnet test
```

The test suite (xUnit + Moq) covers:

- **Search fidelity** — `query_type = semantic` (using the `sem` configuration), `top_k`, and the selected citation fields.
- **Citation payload** — the JSON returned to the agent projects `sourceDoc`, `pageFrom`/`pageTo`, `title`, `url`, `chunk`, and `score`, with missing fields serialized as null.
- **End-to-end tool call** — `SearchAsync` against a mocked `SearchClient` passes the right query/options and serializes hits.
- **ACL policy** — `QueryAclPolicy` attaches the `x-ms-query-source-authorization` header and pins the preview api-version.
- **Instructions** — grounding and citation requirements are present (guards against prompt drift).

## Notes

- The Foundry `azure_ai_search` tool is reproduced as a function tool. The model
  decides when to call it, then formats answers per the instructions.
- Retrieval uses **semantic ranking** via the index's `sem` semantic configuration
  (L2 reranker over the `chunk` content field) for better relevance than keyword search.
- The index also includes a 3072-dim `vector` field (HNSW / cosine) for future vector
  or hybrid search.
