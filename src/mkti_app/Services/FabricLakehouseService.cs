using Azure.Core;
using Azure.Identity;

namespace mkti_app.Services;

public sealed class FabricLakehouseService
{
    private readonly string _workspaceId;
    private readonly string _lakehouseId;
    private readonly TokenCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FabricLakehouseService> _logger;

    public FabricLakehouseService(
        string workspaceId,
        string lakehouseId,
        DefaultAzureCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<FabricLakehouseService> logger)
    {
        _workspaceId = workspaceId;
        _lakehouseId = lakehouseId;
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<object> QueryStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(_workspaceId) || string.IsNullOrWhiteSpace(_lakehouseId))
        {
            return new { connected = false, reason = "Fabric workspace/lakehouse config is not set." };
        }

        try
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(["https://analysis.windows.net/powerbi/api/.default"]),
                CancellationToken.None);
            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.fabric.microsoft.com/v1/workspaces/{_workspaceId}/lakehouses/{_lakehouseId}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            var response = await client.SendAsync(request);
            return new
            {
                connected = response.IsSuccessStatusCode,
                statusCode = (int)response.StatusCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Fabric lakehouse status.");
            return new { connected = false, reason = ex.Message };
        }
    }
}
