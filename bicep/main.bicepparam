using 'main.bicep'

param baseName = 'mkti'
param projectName = 'market_insight'
param location = 'westus3'
param skuName = 'S1'
param fabricSkuName = 'F2'
param primaryModelDeploymentName = 'gpt-4.1'
param secondaryModelDeploymentName = 'gpt-4.1-mini'
param fabricLakehouseWorkspaceId = ''
param fabricLakehouseId = ''
param fabricAdminMembers = [
  // TODO: replace with the UPN/email of the Fabric capacity administrator
  // e.g. 'admin@contoso.com'
]
param principals = [
  {
    id: '4b74544b-02c6-4e4f-b936-732c9c3fff65'
    principalType: 'User'
  }
]

