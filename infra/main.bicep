// Deploys an Azure AI Search service that mirrors the existing `trsdemosearch`
// configuration (Free SKU, Central US, 1 replica / 1 partition, free semantic
// search, system-assigned identity, AAD-or-key auth).
//
// NOTE: The search *index* (documents-index) is a data-plane object and cannot be
// created via ARM/Bicep. After this deployment completes, run
// scripts/create-index.ps1 to create the index schema.

targetScope = 'resourceGroup'

@description('Name of the Azure AI Search service. Must be globally unique.')
param searchServiceName string = 'trsdemosearch'

@description('Location for the search service.')
param location string = 'centralus'

@description('Pricing tier for the search service.')
@allowed([
  'free'
  'basic'
  'standard'
  'standard2'
  'standard3'
])
param sku string = 'free'

@description('Number of replicas. Free SKU supports only 1.')
@minValue(1)
@maxValue(12)
param replicaCount int = 1

@description('Number of partitions. Free SKU supports only 1.')
@allowed([
  1
  2
  3
  4
  6
  12
])
param partitionCount int = 1

@description('Semantic search tier. Free SKU supports the free semantic plan.')
@allowed([
  'disabled'
  'free'
  'standard'
])
param semanticSearch string = 'free'

resource searchService 'Microsoft.Search/searchServices@2024-03-01-preview' = {
  name: searchServiceName
  location: location
  sku: {
    name: sku
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    replicaCount: replicaCount
    partitionCount: partitionCount
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
    semanticSearch: semanticSearch
    disableLocalAuth: false
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http401WithBearerChallenge'
      }
    }
    networkRuleSet: {
      bypass: 'None'
      ipRules: []
    }
  }
}

@description('The endpoint of the deployed search service.')
output searchEndpoint string = 'https://${searchService.name}.search.windows.net'

@description('The resource name of the deployed search service.')
output searchServiceName string = searchService.name

@description('The principal ID of the search service system-assigned identity.')
output searchServicePrincipalId string = searchService.identity.principalId
