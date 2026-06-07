targetScope = 'resourceGroup'

@description('Azure location')
param location string = resourceGroup().location

@description('Short base name / abbreviation for resources (e.g. mkti)')
param baseName string

@description('Full project name used for Foundry and Fabric resources')
param projectName string = 'market_insight'

var skuName = 'S1'
var fabricSkuName = 'F2'

@description('AI model deployment name')
param modelDeploymentName string = 'gpt-5.4'

@description('Additional principals to grant Storage Blob Data Contributor on the storage account')
param principals array = []

@description('UPN/email addresses of Fabric capacity administrators')
param fabricAdminMembers array = []

@description('Fabric Lakehouse workspace ID')
param fabricLakehouseWorkspaceId string = ''

@description('Fabric Lakehouse ID')
param fabricLakehouseId string = ''

@description('Bing Search v7 endpoint')
param bingSearchEndpoint string = 'https://api.bing.microsoft.com/'

var tags = { project: projectName }
var logAnalyticsName = '${baseName}-law'
var appInsightsName = '${baseName}-appi'
var storageAccountName = toLower('${baseName}sa')
var appServicePlanName = '${baseName}-plan'
var webAppName = '${baseName}-web'
var aiProjectName = '${baseName}-proj'
var aiServicesName = '${baseName}-ais'
var bingSearchName = '${baseName}-bing'
var fabricCapacityName = '${baseName}fabric'

module monitoring 'monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    logAnalyticsName: logAnalyticsName
    appInsightsName: appInsightsName
    tags: tags
  }
}

module storage 'storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    storageAccountName: storageAccountName
    tags: tags
  }
}

module foundry 'foundry.bicep' = {
  name: 'foundry'
  params: {
    location: location
    aiServicesName: aiServicesName
    aiProjectName: aiProjectName
    modelDeploymentName: modelDeploymentName
  }
}

module bing 'bing.bicep' = {
  name: 'bing'
  params: {
    bingSearchName: bingSearchName
  }
}

module fabric 'fabric.bicep' = {
  name: 'fabric'
  params: {
    location: location
    capacityName: fabricCapacityName
    skuName: fabricSkuName
    adminMembers: fabricAdminMembers
  }
}

module appService 'appservice.bicep' = {
  name: 'appservice'
  params: {
    location: location
    webAppName: webAppName
    appServicePlanName: appServicePlanName
    appServiceSku: skuName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    storageAccountName: storageAccountName
    aiProjectEndpoint: foundry.outputs.aiProjectEndpoint
    modelDeploymentName: foundry.outputs.modelDeploymentName
    docIntelligenceEndpoint: foundry.outputs.aiServicesEndpoint
    fabricLakehouseWorkspaceId: fabricLakehouseWorkspaceId
    fabricLakehouseId: fabricLakehouseId
    bingSearchApiKey: bing.outputs.apiKey
    bingSearchEndpoint: bingSearchEndpoint
  }
}

module storageRoles 'storageroles.bicep' = {
  name: 'storageroles'
  params: {
    storageAccountName: storageAccountName
    webAppPrincipalId: appService.outputs.principalId
    webAppName: webAppName
    principals: principals
  }
  dependsOn: [storage]
}

module foundryRoles 'foundryroles.bicep' = {
  name: 'foundryroles'
  params: {
    aiServicesName: aiServicesName
    aiProjectName: aiProjectName
    webAppPrincipalId: appService.outputs.principalId
    webAppName: webAppName
    principals: principals
  }
}

resource aiServicesAccount 'Microsoft.CognitiveServices/accounts@2024-04-01-preview' existing = {
  name: aiServicesName
}

// Grant the web app Cognitive Services User access (used by Document Intelligence)
resource cognitiveServicesUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: aiServicesAccount
  name: guid(aiServicesName, webAppName, 'a97b65f3-24c7-4388-baec-2e87135dc908')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
    principalId: appService.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant principals Cognitive Services User access on AI Services
resource principalCognitiveServicesAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
  scope: aiServicesAccount
  name: guid(aiServicesName, principal.id, 'a97b65f3-24c7-4388-baec-2e87135dc908')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
    principalId: principal.id
    principalType: principal.principalType
  }
}]

output webAppName string = appService.outputs.webAppName
output webAppUrl string = appService.outputs.webAppUrl
output storageAccountName string = storage.outputs.storageAccountName
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output azureAiProjectEndpoint string = foundry.outputs.aiProjectEndpoint
output azureAiModelDeploymentName string = foundry.outputs.modelDeploymentName
output fabricCapacityName string = fabric.outputs.fabricCapacityName
