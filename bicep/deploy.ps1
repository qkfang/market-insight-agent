
az group create --name 'rg-market-insight' --location 'westus3'

$deployOutput = az deployment group create --name 'mkti-deploy' --resource-group 'rg-market-insight' --template-file './main.bicep' --parameters './main.bicepparam' --query 'properties.outputs' -o json | ConvertFrom-Json


