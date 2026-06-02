param bingSearchName string

#disable-next-line BCP081
resource bingSearch 'Microsoft.Bing/accounts@2020-06-10' = {
  name: bingSearchName
  location: 'global'
  sku: {
    name: 'G1'
  }
  kind: 'Bing.Grounding'
}

output resourceId string = bingSearch.id
output externalId string = bingSearch.id
output apiKey string = listKeys(bingSearch.id, '2020-06-10').key1
