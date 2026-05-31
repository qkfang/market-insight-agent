targetScope = 'resourceGroup'

@description('Azure location')
param location string = resourceGroup().location

@description('Short base name / abbreviation for resources (e.g. mkti)')
param baseName string

@description('Full project name used for Foundry and Fabric resources')
param projectName string = 'market_insight'

@description('SKU for App Service plan')
@allowed([
  'F1'
  'B1'
  'S1'
])
param skuName string = 'S1'

@description('Microsoft Fabric capacity SKU name')
@allowed(['F2', 'F4', 'F8', 'F16', 'F32', 'F64', 'F128', 'F256', 'F512', 'F1024', 'F2048'])
param fabricSkuName string = 'F2'

@description('Primary AI model deployment name')
param primaryModelDeploymentName string = 'gpt-4.1'

@description('Secondary AI model deployment name')
param secondaryModelDeploymentName string = 'gpt-4.1-mini'

@description('Admin UPN/email addresses for the Fabric capacity')
param fabricAdminMembers array = []

@description('Additional principals to grant Storage Blob Data Contributor on the storage account')
param principals array = []

@description('Fabric Data Agent MCP URL')
param fabricMcpUrl string = ''

@description('Bing Search v7 API key')
@secure()
param bingSearchApiKey string = ''

@description('Bing Search v7 endpoint')
param bingSearchEndpoint string = 'https://api.bing.microsoft.com/'

var uniqueSuffix = uniqueString(resourceGroup().id)
var tags = { project: projectName }
var logAnalyticsName = '${baseName}-law'
var appInsightsName = '${baseName}-appi'
var storageAccountName = toLower('${baseName}sa${take(uniqueSuffix, 4)}')
var appServicePlanName = '${baseName}-plan'
var webAppName = '${baseName}-web'
var hubName = '${baseName}-hub'
var aiProjectName = '${baseName}-proj'
var aiServicesName = '${baseName}-ais'
var keyVaultName = '${baseName}-kv-${take(uniqueSuffix, 4)}'
var fabricCapacityName = '${baseName}-fabric'

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
  }
}

module foundry 'foundry.bicep' = {
  name: 'foundry'
  params: {
    location: location
    hubName: hubName
    aiProjectName: aiProjectName
    aiServicesName: aiServicesName
    keyVaultName: keyVaultName
    storageAccountId: storage.outputs.storageAccountId
    appInsightsId: monitoring.outputs.appInsightsId
    primaryModelDeploymentName: primaryModelDeploymentName
    secondaryModelDeploymentName: secondaryModelDeploymentName
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
    primaryModelDeploymentName: foundry.outputs.primaryModelDeploymentName
    docIntelligenceEndpoint: foundry.outputs.aiServicesEndpoint
    fabricMcpUrl: fabricMcpUrl
    bingSearchApiKey: bingSearchApiKey
    bingSearchEndpoint: bingSearchEndpoint
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource blobDataContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccountName, webAppName, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: appService.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource principalBlobDataContributorAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
  scope: storageAccount
  name: guid(storageAccountName, principal.id, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: principal.id
    principalType: principal.principalType
  }
}]

resource aiServicesAccount 'Microsoft.CognitiveServices/accounts@2024-04-01-preview' existing = {
  name: aiServicesName
}

// Grant the web app Cognitive Services User access (used by Document Intelligence)
resource cognitiveServicesUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: aiServicesAccount
  name: guid(aiServicesName, webAppName, 'a97b65f3-24c7-4388-baec-2e87618995b6')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87618995b6')
    principalId: appService.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

output webAppName string = appService.outputs.webAppName
output webAppUrl string = appService.outputs.webAppUrl
output storageAccountName string = storage.outputs.storageAccountName
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output azureAiProjectEndpoint string = foundry.outputs.aiProjectEndpoint
output azureAiModelDeploymentName string = foundry.outputs.primaryModelDeploymentName
output fabricCapacityName string = fabric.outputs.fabricCapacityName
