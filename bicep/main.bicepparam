using 'main.bicep'

param baseName = 'mkti'
param projectName = 'market_insight'
param location = 'westus3'
param primaryModelDeploymentName = 'gpt-4.1'
param principals = [
  {
    id: '4b74544b-02c6-4e4f-b936-732c9c3fff65'
    principalType: 'User'
  }
]
param fabricAdminMembers = [
  'danielfang@MngEnvMCAP951655.onmicrosoft.com'
  'fabric@MngEnvMCAP951655.onmicrosoft.com'
]

param fabricLakehouseWorkspaceId = 'b4b2a30e-7ca8-4843-8dd8-bf84e283e025'
param fabricLakehouseId = '4d1ce629-360a-4b24-aa5e-04f81a76c81a'

