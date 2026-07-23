<#
.SYNOPSIS
    Provisions the Azure AI Content Understanding ingestion pipeline on the
    search service: a chunk index, a skillset (Content Understanding skill +
    Azure OpenAI embedding + index projections), a blob data source, and an
    indexer.

.DESCRIPTION
    Uses the 2026-05-01-preview REST API, which is required for the
    Content Understanding skill (semantic chunking, AI image descriptions,
    and location metadata).

    Before running, edit the infra/content-understanding-*.json files and
    replace the placeholders:
      - content-understanding-datasource.json: storage account resource ID
      - content-understanding-skillset.json:
          * modelDeployment  (Azure OpenAI chat deployment for image descriptions)
          * resourceUri / deploymentId (Azure OpenAI embedding deployment)
          * cognitiveServices.subdomainUrl (Foundry resource attached to the skillset)

    The examples use managed identity (AIServicesByIdentity). Grant the search
    service's identity access to the Foundry resource, the Azure OpenAI resource,
    and the storage account. See the Azure AI Search RBAC documentation.

.EXAMPLE
    ./create-content-understanding-index.ps1 -SearchServiceName trsdemosearch -ResourceGroup TRSDemo -RunIndexer
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SearchServiceName,

    [Parameter()]
    [string]$ResourceGroup,

    [Parameter()]
    [string]$AdminKey,

    [Parameter()]
    [string]$ApiVersion = '2026-05-01-preview',

    [Parameter()]
    [string]$InfraDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'infra'),

    [Parameter()]
    [switch]$RunIndexer
)

$ErrorActionPreference = 'Stop'

if (-not $AdminKey) {
    if (-not $ResourceGroup) {
        throw 'Provide either -AdminKey or -ResourceGroup (to fetch the admin key via Azure CLI).'
    }
    Write-Host "Retrieving admin key for '$SearchServiceName'..."
    $AdminKey = az search admin-key show `
        --service-name $SearchServiceName `
        --resource-group $ResourceGroup `
        --query primaryKey -o tsv
    if (-not $AdminKey) { throw 'Failed to retrieve the search admin key.' }
}

$base = "https://$SearchServiceName.search.windows.net"
$headers = @{ 'api-key' = $AdminKey; 'Content-Type' = 'application/json' }

function Invoke-Put([string]$resourcePath, [string]$file) {
    $path = Join-Path $InfraDir $file
    if (-not (Test-Path $path)) { throw "Definition not found: $path" }
    $body = Get-Content -Raw -Path $path
    $name = (ConvertFrom-Json $body).name
    $uri = "$base/$resourcePath/$name`?api-version=$ApiVersion"
    Write-Host "PUT $resourcePath/$name ..."
    Invoke-RestMethod -Uri $uri -Method Put -Headers $headers -Body $body | Out-Null
}

# Order matters: index, skillset, and data source must exist before the indexer.
Invoke-Put -resourcePath 'indexes'      -file 'content-understanding-index.json'
Invoke-Put -resourcePath 'skillsets'    -file 'content-understanding-skillset.json'
Invoke-Put -resourcePath 'datasources'  -file 'content-understanding-datasource.json'
Invoke-Put -resourcePath 'indexers'     -file 'content-understanding-indexer.json'

Write-Host "Content Understanding index, skillset, data source, and indexer created." -ForegroundColor Green

if ($RunIndexer) {
    Write-Host 'Triggering indexer run...'
    Invoke-RestMethod -Uri "$base/indexers/content-understanding-indexer/run?api-version=$ApiVersion" `
        -Method Post -Headers $headers | Out-Null
    Write-Host 'Indexer run started. Check status with the /status endpoint.' -ForegroundColor Green
}
