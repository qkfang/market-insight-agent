@description('Azure location')
param location string

@description('Fabric capacity resource name')
param capacityName string

@description('Fabric capacity SKU name')
@allowed(['F2', 'F4', 'F8', 'F16', 'F32', 'F64', 'F128', 'F256', 'F512', 'F1024', 'F2048'])
param skuName string = 'F2'

@description('Array of admin UPN/email addresses for the Fabric capacity')
param adminMembers array

resource fabricCapacity 'Microsoft.Fabric/capacities@2023-11-01' = {
  name: capacityName
  location: location
  sku: {
    name: skuName
    tier: 'Fabric'
  }
  properties: {
    administration: {
      members: adminMembers
    }
  }
}

output fabricCapacityId string = fabricCapacity.id
output fabricCapacityName string = fabricCapacity.name
