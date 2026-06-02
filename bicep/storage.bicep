@description('Azure location')
param location string

@description('Storage account name')
param storageAccountName string

@description('Resource tags')
param tags object = {}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    publicNetworkAccess: 'Enabled'
  }
}

resource newsStoreContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${storage.name}/default/news-store'
  properties: {
    publicAccess: 'None'
  }
}

resource newsAnalysisContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${storage.name}/default/news-analysis'
  properties: {
    publicAccess: 'None'
  }
}

resource marketInsightContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${storage.name}/default/market-insight'
  properties: {
    publicAccess: 'None'
  }
}

output storageAccountName string = storage.name
output storageAccountId string = storage.id
