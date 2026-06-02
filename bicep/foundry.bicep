@description('Azure location')
param location string

@description('AI Services account name (serves as Foundry hub)')
param aiServicesName string

@description('AI Foundry project name')
param aiProjectName string

@description('Primary model deployment name (gpt-4.1)')
param primaryModelDeploymentName string = 'gpt-4.1'

@description('Secondary model deployment name (gpt-4.1-mini)')
param secondaryModelDeploymentName string = 'gpt-4.1-mini'

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

// gpt-4.1 model deployment
resource gpt41Deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: aiHub
  name: primaryModelDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1'
      version: '2025-04-14'
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

// gpt-4.1-mini model deployment
resource gpt41MiniDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: aiHub
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
      version: '2025-04-14'
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

output aiProjectEndpoint string = aiProject.properties.endpoints['AI Foundry API']
output aiServicesEndpoint string = aiHub.properties.endpoint
output primaryModelDeploymentName string = gpt41Deployment.name
output secondaryModelDeploymentName string = gpt41MiniDeployment.name
output aiHubPrincipalId string = aiHub.identity.principalId
