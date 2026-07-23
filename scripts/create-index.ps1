<#
.SYNOPSIS
    Creates (or updates) the `documents-index` Azure AI Search index used by the
    AI Search Agent.

.DESCRIPTION
    The search index is a data-plane object and cannot be provisioned with
    ARM/Bicep, so this script creates it via the Search REST API after the
    service has been deployed (see infra/main.bicep).

    Authentication uses the service admin key. The key is retrieved with the
    Azure CLI unless you pass -AdminKey explicitly.

.EXAMPLE
    ./create-index.ps1 -SearchServiceName trsdemosearch -ResourceGroup TRSDemo

.EXAMPLE
    ./create-index.ps1 -SearchServiceName trsdemosearch -AdminKey $env:SEARCH_ADMIN_KEY
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
    [string]$ApiVersion = '2024-07-01',

    [Parameter()]
    [string]$IndexDefinitionPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'infra/documents-index.json')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $IndexDefinitionPath)) {
    throw "Index definition not found at '$IndexDefinitionPath'."
}

if (-not $AdminKey) {
    if (-not $ResourceGroup) {
        throw 'Provide either -AdminKey or -ResourceGroup (to fetch the admin key via Azure CLI).'
    }
    Write-Host "Retrieving admin key for '$SearchServiceName'..."
    $AdminKey = az search admin-key show `
        --service-name $SearchServiceName `
        --resource-group $ResourceGroup `
        --query primaryKey -o tsv
    if (-not $AdminKey) {
        throw 'Failed to retrieve the search admin key.'
    }
}

$indexJson = Get-Content -Raw -Path $IndexDefinitionPath
$indexName = (ConvertFrom-Json $indexJson).name
$uri = "https://$SearchServiceName.search.windows.net/indexes/$indexName?api-version=$ApiVersion"

$headers = @{
    'api-key'      = $AdminKey
    'Content-Type' = 'application/json'
}

Write-Host "Creating/updating index '$indexName' on '$SearchServiceName'..."
Invoke-RestMethod -Uri $uri -Method Put -Headers $headers -Body $indexJson | Out-Null
Write-Host "Index '$indexName' is ready." -ForegroundColor Green
