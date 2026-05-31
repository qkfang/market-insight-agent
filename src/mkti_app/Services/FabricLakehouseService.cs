using Azure.Core;
using Azure.Identity;
using System.Net.Http.Headers;

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

    public async Task<string?> UploadFileAsync(string folderPath, string filename, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(_workspaceId) || string.IsNullOrWhiteSpace(_lakehouseId))
        {
            _logger.LogInformation(
                "Fabric workspace/lakehouse config is not set; skipping OneLake upload for {Folder}/{File}.",
                folderPath, filename);
            return null;
        }

        try
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(["https://storage.azure.com/.default"]),
                CancellationToken.None);

            var client = _httpClientFactory.CreateClient();
            var relativePath = $"{folderPath.Trim('/')}/{filename.TrimStart('/')}";
            var baseUri = $"https://onelake.dfs.fabric.microsoft.com/{_workspaceId}/{_lakehouseId}/Files/{relativePath}";

            // 1. Create the (empty) file.
            using (var createRequest = new HttpRequestMessage(HttpMethod.Put, $"{baseUri}?resource=file"))
            {
                createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                var createResponse = await client.SendAsync(createRequest);
                createResponse.EnsureSuccessStatusCode();
            }

            // 2. Append the content at position 0.
            using (var appendRequest = new HttpRequestMessage(new HttpMethod("PATCH"), $"{baseUri}?action=append&position=0"))
            {
                appendRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                appendRequest.Content = new ByteArrayContent(content);
                appendRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                var appendResponse = await client.SendAsync(appendRequest);
                appendResponse.EnsureSuccessStatusCode();
            }

            // 3. Flush the appended content to commit the file.
            using (var flushRequest = new HttpRequestMessage(new HttpMethod("PATCH"), $"{baseUri}?action=flush&position={content.Length}"))
            {
                flushRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                var flushResponse = await client.SendAsync(flushRequest);
                flushResponse.EnsureSuccessStatusCode();
            }

            return $"{_workspaceId}/{_lakehouseId}/Files/{relativePath}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload file to Fabric OneLake: {Folder}/{File}", folderPath, filename);
            return null;
        }
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
