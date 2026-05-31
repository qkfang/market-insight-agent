@description('Azure location')
param location string

@description('Web App name')
param webAppName string

@description('App Service plan name')
param appServicePlanName string

@description('SKU for App Service plan')
param appServiceSku string

@description('App Insights connection string')
param appInsightsConnectionString string

@description('Storage account name for blob storage')
param storageAccountName string

@description('Azure AI project endpoint')
param aiProjectEndpoint string

@description('Primary AI model deployment name')
param primaryModelDeploymentName string

@description('Azure Document Intelligence endpoint')
param docIntelligenceEndpoint string = ''

@description('Fabric Data Agent MCP URL')
param fabricMcpUrl string = ''

@description('Fabric Lakehouse workspace ID')
param fabricLakehouseWorkspaceId string = ''

@description('Fabric Lakehouse ID')
param fabricLakehouseId string = ''

@description('Bing Search v7 API key')
@secure()
param bingSearchApiKey string = ''

@description('Bing Search v7 endpoint')
param bingSearchEndpoint string = 'https://api.bing.microsoft.com/'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: appServiceSku
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet mkti_app.dll'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'false'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'AZURE_AI_PROJECT_ENDPOINT'
          value: aiProjectEndpoint
        }
        {
          name: 'AZURE_AI_MODEL_DEPLOYMENT_NAME'
          value: primaryModelDeploymentName
        }
        {
          name: 'AZURE_STORAGE_ACCOUNT_NAME'
          value: storageAccountName
        }
        {
          name: 'AZURE_STORAGE_ACCOUNT_URL'
          value: 'https://${storageAccountName}.blob.${environment().suffixes.storage}'
        }
        {
          name: 'AZURE_DOC_INTELLIGENCE_ENDPOINT'
          value: docIntelligenceEndpoint
        }
        {
          name: 'FABRIC_MCP_URL'
          value: fabricMcpUrl
        }
        {
          name: 'FABRIC_LAKEHOUSE_WORKSPACE_ID'
          value: fabricLakehouseWorkspaceId
        }
        {
          name: 'FABRIC_LAKEHOUSE_ID'
          value: fabricLakehouseId
        }
        {
          name: 'APP_MCP_URL'
          value: 'https://${webAppName}.azurewebsites.net'
        }
        {
          name: 'BING_SEARCH_API_KEY'
          value: bingSearchApiKey
        }
        {
          name: 'BING_SEARCH_ENDPOINT'
          value: bingSearchEndpoint
        }
      ]
    }
    httpsOnly: true
  }
}

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output principalId string = webApp.identity.principalId
