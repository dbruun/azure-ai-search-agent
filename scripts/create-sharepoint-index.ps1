<#
.SYNOPSIS
    Provisions the SharePoint-backed, ACL-enabled search pipeline on the
    `trsdemosearch` service: a permission-filtered index, a SharePoint data
    source, and an indexer that ingests document ACLs (preview).

.DESCRIPTION
    Uses the 2026-05-01-preview REST API, which is required for:
      - permissionFilterOption / permissionFilter index fields
      - SharePoint data source `indexerPermissionOptions`
      - SharePoint groups resolution (spg: prefixed group IDs)

    Before running, edit infra/sharepoint-datasource.json and replace the
    connection-string placeholders (SharePoint site URL, Entra ApplicationId,
    TenantId). The Entra app must be granted the SharePoint/Graph permissions
    described in the Azure AI Search SharePoint indexer documentation.

.EXAMPLE
    ./create-sharepoint-index.ps1 -SearchServiceName trsdemosearch -ResourceGroup TRSDemo -RunIndexer
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
    [string]$InfraDir = $PSScriptRoot ? (Join-Path (Split-Path $PSScriptRoot -Parent) 'infra') : 'infra',

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

# Order matters: index and data source must exist before the indexer.
Invoke-Put -resourcePath 'indexes'      -file 'sharepoint-index.json'
Invoke-Put -resourcePath 'datasources'  -file 'sharepoint-datasource.json'
Invoke-Put -resourcePath 'indexers'     -file 'sharepoint-indexer.json'

Write-Host "SharePoint ACL index, data source, and indexer created." -ForegroundColor Green

if ($RunIndexer) {
    Write-Host 'Triggering indexer run...'
    Invoke-RestMethod -Uri "$base/indexers/sharepoint-indexer/run?api-version=$ApiVersion" `
        -Method Post -Headers $headers | Out-Null
    Write-Host 'Indexer run started. Check status with the /status endpoint.' -ForegroundColor Green
}
