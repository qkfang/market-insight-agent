@description('AI Services account name (Foundry hub)')
param aiServicesName string

@description('AI Foundry project name')
param aiProjectName string

@description('Principal ID of the web app managed identity')
param webAppPrincipalId string

@description('Web app name used for role assignment guid')
param webAppName string

@description('Additional principals to grant Azure AI Developer')
param principals array = []

resource aiHub 'Microsoft.CognitiveServices/accounts@2025-10-01-preview' existing = {
  name: aiServicesName
}

resource aiProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: aiHub
  name: aiProjectName
}

// Azure AI Developer — grants Microsoft.MachineLearningServices/workspaces/agents/action
// required for the Foundry Agents API
var azureAiDeveloperRoleId = '64702f94-c441-49e6-a78b-ef80e0188fee'

resource webAppAiDeveloperAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: aiProject
  name: guid(aiServicesName, aiProjectName, webAppName, azureAiDeveloperRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', azureAiDeveloperRoleId)
    principalId: webAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource principalAiDeveloperAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
  scope: aiProject
  name: guid(aiServicesName, aiProjectName, principal.id, azureAiDeveloperRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', azureAiDeveloperRoleId)
    principalId: principal.id
    principalType: principal.principalType
  }
}]
