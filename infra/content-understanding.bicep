// Provisions the backing resources for the Content Understanding pipeline:
//   - An Azure AI Services (Foundry) account (kind: AIServices) that provides
//     both Content Understanding and Azure OpenAI model deployments.
//   - A gpt-4o chat deployment (used for AI image descriptions) and a
//     text-embedding-3-large deployment (used to vectorize chunks).
//   - A Storage account + blob container for source documents.
//   - Role assignments granting the search service's managed identity the
//     access it needs to call the skillset resources and read the blobs.
//
// Deploy AFTER main.bicep, passing that deployment's searchServicePrincipalId.
//
// NOTE: The index, skillset, data source, and indexer are data-plane objects.
// After this deployment, run scripts/create-content-understanding-index.ps1.

targetScope = 'resourceGroup'

@description('Location for all resources.')
param location string = resourceGroup().location

@description('Name of the Azure AI Services (Foundry) account. Must be globally unique.')
param aiServicesName string

@description('Name of the Storage account for source documents. 3-24 lowercase alphanumerics, globally unique.')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Blob container that holds source documents to index.')
param documentsContainerName string = 'documents'

@description('Principal ID of the Azure AI Search service system-assigned identity (output of main.bicep).')
param searchServicePrincipalId string

@description('Chat model deployment name used for AI image descriptions.')
param chatDeploymentName string = 'gpt-4o'

@description('Chat model version.')
param chatModelVersion string = '2024-11-20'

@description('Embedding model deployment name used to vectorize chunks.')
param embeddingDeploymentName string = 'text-embedding-3-large'

// ---------------------------------------------------------------------------
// Well-known built-in role definition IDs
// ---------------------------------------------------------------------------
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908' // Cognitive Services User (Content Understanding)
var cognitiveServicesOpenAIUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd' // Cognitive Services OpenAI User (embeddings)
var storageBlobDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1' // Storage Blob Data Reader (source docs)

// ---------------------------------------------------------------------------
// Azure AI Services (Foundry) account — Content Understanding + Azure OpenAI
// ---------------------------------------------------------------------------
resource aiServices 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: aiServicesName
  location: location
  sku: {
    name: 'S0'
  }
  kind: 'AIServices'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: aiServicesName
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: aiServices
  name: chatDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: 50
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: chatModelVersion
    }
  }
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: aiServices
  name: embeddingDeploymentName
  sku: {
    name: 'Standard'
    capacity: 50
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: '1'
    }
  }
  // Deployments on the same account must be created sequentially.
  dependsOn: [
    chatDeployment
  ]
}

// ---------------------------------------------------------------------------
// Storage for source documents
// ---------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: documentsContainerName
  properties: {
    publicAccess: 'None'
  }
}

// ---------------------------------------------------------------------------
// Role assignments — grant the search service identity access to the skillset
// resources (AI Services + Azure OpenAI) and read access to the source blobs.
// ---------------------------------------------------------------------------
resource searchToAiServices 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiServices.id, searchServicePrincipalId, cognitiveServicesUserRoleId)
  scope: aiServices
  properties: {
    principalId: searchServicePrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
  }
}

resource searchToOpenAI 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiServices.id, searchServicePrincipalId, cognitiveServicesOpenAIUserRoleId)
  scope: aiServices
  properties: {
    principalId: searchServicePrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAIUserRoleId)
  }
}

resource searchToStorage 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, searchServicePrincipalId, storageBlobDataReaderRoleId)
  scope: storage
  properties: {
    principalId: searchServicePrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReaderRoleId)
  }
}

// ---------------------------------------------------------------------------
// Outputs — plug these into infra/content-understanding-*.json
// ---------------------------------------------------------------------------
@description('AI Services (Foundry) endpoint, e.g. https://<name>.cognitiveservices.azure.com/. Use for cognitiveServices.subdomainUrl and the embedding resourceUri.')
output aiServicesEndpoint string = aiServices.properties.endpoint

@description('AI Services account name.')
output aiServicesName string = aiServices.name

@description('Chat deployment name (modelDeployment for image descriptions).')
output chatDeploymentName string = chatDeployment.name

@description('Embedding deployment name (deploymentId in the embedding skill).')
output embeddingDeploymentName string = embeddingDeployment.name

@description('Storage account resource ID (for the data source connection string ResourceId=...).')
output storageAccountId string = storage.id

@description('Documents container name.')
output documentsContainerName string = documentsContainer.name
