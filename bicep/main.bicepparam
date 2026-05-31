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

