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
  {
    id: '6c2ec22f-9401-452f-8d04-10bfddfab00f'
    principalType: 'ServicePrincipal'
  }
]
param fabricAdminMembers = [
  'danielfang@MngEnvMCAP951655.onmicrosoft.com'
  'fabric@MngEnvMCAP951655.onmicrosoft.com'
  'sp-playground-01'
]

param fabricLakehouseWorkspaceId = 'b4b2a30e-7ca8-4843-8dd8-bf84e283e025'
param fabricLakehouseId = '4d1ce629-360a-4b24-aa5e-04f81a76c81a'

// Existing logged-in deployment service principal
// appId: 0e872ca6-4149-4c58-9b10-64121fe089a5
// objectId: 6c2ec22f-9401-452f-8d04-10bfddfab00f

