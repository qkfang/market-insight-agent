@description('Azure location')
param location string

@description('AI Foundry hub workspace name')
param hubName string

@description('AI Foundry project workspace name')
param aiProjectName string

@description('Azure AI Services account name')
param aiServicesName string

@description('Key Vault name (required by AI Foundry hub)')
param keyVaultName string

@description('Storage account resource ID for AI Foundry hub')
param storageAccountId string

@description('Application Insights resource ID for AI Foundry hub')
param appInsightsId string

@description('Primary model deployment name (gpt-4.1)')
param primaryModelDeploymentName string = 'gpt-4.1'

@description('Secondary model deployment name (gpt-4.1-mini)')
param secondaryModelDeploymentName string = 'gpt-4.1-mini'

// Key Vault required by AI Foundry hub
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enableRbacAuthorization: true
  }
}

// Azure AI Services account (provides OpenAI model deployments)
resource aiServices 'Microsoft.CognitiveServices/accounts@2024-04-01-preview' = {
  name: aiServicesName
  location: location
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: aiServicesName
    publicNetworkAccess: 'Enabled'
  }
}

// gpt-4.1 model deployment
resource gpt41Deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-04-01-preview' = {
  parent: aiServices
  name: primaryModelDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1'
    }
  }
}

// gpt-4.1-mini model deployment
resource gpt41MiniDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-04-01-preview' = {
  parent: aiServices
  name: secondaryModelDeploymentName
  dependsOn: [gpt41Deployment]
  sku: {
    name: 'GlobalStandard'
    capacity: 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1-mini'
    }
  }
}

// Azure AI Foundry Hub
resource hub 'Microsoft.MachineLearningServices/workspaces@2024-07-01-preview' = {
  name: hubName
  location: location
  kind: 'Hub'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    friendlyName: hubName
    storageAccount: storageAccountId
    keyVault: keyVault.id
    applicationInsights: appInsightsId
    publicNetworkAccess: 'Enabled'
  }
}

// Azure AI Foundry Project
resource aiProject 'Microsoft.MachineLearningServices/workspaces@2024-07-01-preview' = {
  name: aiProjectName
  location: location
  kind: 'Project'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    friendlyName: aiProjectName
    hubResourceId: hub.id
    publicNetworkAccess: 'Enabled'
  }
}

output aiProjectEndpoint string = aiProject.properties.discoveryUrl
output aiServicesEndpoint string = aiServices.properties.endpoint
output primaryModelDeploymentName string = gpt41Deployment.name
output secondaryModelDeploymentName string = gpt41MiniDeployment.name
