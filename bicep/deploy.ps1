
$projectName    = 'market-insight'
$baseName       = 'mkti'
$location       = 'westus3'
$subscriptionId = az account show --query 'id' -o tsv
$resourceGroup  = "rg-$projectName"

az account set --subscription $subscriptionId

az group create --name $resourceGroup --location $location

$deployOutput = az deployment group create `
  --name "$baseName-deploy" `
  --resource-group $resourceGroup `
  --template-file './main.bicep' `
  --parameters './main.bicepparam' `
  --query 'properties.outputs' `
  -o json | ConvertFrom-Json

Write-Host ""
Write-Host "=== Deployment Outputs ===" -ForegroundColor Cyan
Write-Host "Web App Name:                 $($deployOutput.webAppName.value)"
Write-Host "Web App URL:                  $($deployOutput.webAppUrl.value)"
Write-Host "Storage Account Name:         $($deployOutput.storageAccountName.value)"
Write-Host "AppInsights Connection String: $($deployOutput.appInsightsConnectionString.value)"
Write-Host "AZURE_AI_PROJECT_ENDPOINT:    $($deployOutput.azureAiProjectEndpoint.value)"
Write-Host "AZURE_AI_MODEL_DEPLOYMENT_NAME: $($deployOutput.azureAiModelDeploymentName.value)"
Write-Host "Fabric Capacity Name:         $($deployOutput.fabricCapacityName.value)"
Write-Host "==========================" -ForegroundColor Cyan

