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

    public async Task<bool> WriteFileAsync(string relativePath, string content)
    {
        if (string.IsNullOrWhiteSpace(_workspaceId) || string.IsNullOrWhiteSpace(_lakehouseId))
        {
            _logger.LogInformation("Fabric workspace/lakehouse config is not set; skipping OneLake write for {Path}.", Sanitize(relativePath));
            return false;
        }

        try
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(["https://storage.azure.com/.default"]),
                CancellationToken.None);
            var client = _httpClientFactory.CreateClient();

            var safePath = string.Join('/', relativePath
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => part != "." && part != ".."));
            var baseUri = $"https://onelake.dfs.fabric.microsoft.com/{_workspaceId}/{_lakehouseId}/Files/{safePath}";
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);

            var createRequest = new HttpRequestMessage(HttpMethod.Put, $"{baseUri}?resource=file");
            createRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
            var createResponse = await client.SendAsync(createRequest);
            if (!createResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to create OneLake file {Path}: {Status}", Sanitize(safePath), (int)createResponse.StatusCode);
                return false;
            }

            var appendRequest = new HttpRequestMessage(HttpMethod.Patch, $"{baseUri}?action=append&position=0")
            {
                Content = new ByteArrayContent(bytes)
            };
            appendRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
            var appendResponse = await client.SendAsync(appendRequest);
            if (!appendResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to append OneLake file {Path}: {Status}", Sanitize(safePath), (int)appendResponse.StatusCode);
                return false;
            }

            var flushRequest = new HttpRequestMessage(HttpMethod.Patch, $"{baseUri}?action=flush&position={bytes.Length}");
            flushRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
            var flushResponse = await client.SendAsync(flushRequest);
            if (!flushResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to flush OneLake file {Path}: {Status}", Sanitize(safePath), (int)flushResponse.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write file to Fabric lakehouse: {Path}", Sanitize(relativePath));
            return false;
        }
    }

    private static string Sanitize(string value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');

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
