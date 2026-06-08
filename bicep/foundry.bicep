@description('Azure location')
param location string

@description('AI Services account name (serves as Foundry hub)')
param aiServicesName string

@description('AI Foundry project name')
param aiProjectName string

@description('Model deployment name (gpt-5.4)')
param modelDeploymentName string = 'gpt-5.4'

// Azure AI Services account with project management enabled
resource aiHub 'Microsoft.CognitiveServices/accounts@2025-10-01-preview' = {
  name: aiServicesName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'S0'
  }
  kind: 'AIServices'
  properties: {
    allowProjectManagement: true
    customSubDomainName: aiServicesName
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

// Azure AI Foundry Project
resource aiProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: aiHub
  name: aiProjectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

// gpt-5.4 model deployment
resource gpt54Deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: aiHub
  name: modelDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: 900
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-5.4'
      version: '2026-03-05'
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

output aiProjectEndpoint string = aiProject.properties.endpoints['AI Foundry API']
output aiServicesEndpoint string = aiHub.properties.endpoint
output modelDeploymentName string = gpt54Deployment.name
output aiHubPrincipalId string = aiHub.identity.principalId
