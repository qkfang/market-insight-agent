
az group create --name 'rg-market-insight' --location 'westus3'

az deployment group create --name 'mkti-deploy' --resource-group 'rg-market-insight' --template-file './main.bicep' --parameters './main.bicepparam' --query 'properties.outputs' -o json | ConvertFrom-Json


$spObjectId = '6c2ec22f-9401-452f-8d04-10bfddfab00f'  # sp-playground-01
$subscriptionId = az account show --query 'id' -o tsv
az role assignment create --assignee-object-id $spObjectId --assignee-principal-type ServicePrincipal --role 'Contributor' --scope "/subscriptions/$subscriptionId/resourceGroups/rg-market-insight"
az role assignment create --assignee-object-id $spObjectId --assignee-principal-type ServicePrincipal --role 'User Access Administrator' --scope "/subscriptions/$subscriptionId/resourceGroups/rg-market-insight"
