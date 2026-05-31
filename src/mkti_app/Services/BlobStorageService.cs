using Azure.Identity;
using Azure.Storage.Blobs;

namespace mkti_app.Services;

public sealed class BlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly string _fallbackRoot;

    public BlobStorageService(string storageAccountName, DefaultAzureCredential credential, ILogger<BlobStorageService> logger)
    {
        _logger = logger;
        _fallbackRoot = Path.Combine(Path.GetTempPath(), "mkti_app_blob");
        Directory.CreateDirectory(_fallbackRoot);

        var accountUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
        _blobServiceClient = new BlobServiceClient(accountUri, credential);
    }

    public async Task WriteTextAsync(string containerName, string blobName, string content, string? contentType = null)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            await container.CreateIfNotExistsAsync();
            var blob = container.GetBlobClient(blobName);
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            var options = new Azure.Storage.Blobs.Models.BlobUploadOptions();
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                options.HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType };
            }
            await blob.UploadAsync(stream, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to local storage for write to {Container}/{Blob}", Sanitize(containerName), Sanitize(blobName));
            var fullPath = GetFallbackPath(containerName, blobName);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, content);
        }
    }

    public async Task<string?> ReadTextAsync(string containerName, string blobName)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            var blob = container.GetBlobClient(blobName);
            if (!await blob.ExistsAsync())
                return null;

            var download = await blob.DownloadContentAsync();
            return download.Value.Content.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to local storage for read from {Container}/{Blob}", Sanitize(containerName), Sanitize(blobName));
            var fullPath = GetFallbackPath(containerName, blobName);
            return File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath) : null;
        }
    }

    public async Task<byte[]?> ReadBytesAsync(string containerName, string blobName)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            var blob = container.GetBlobClient(blobName);
            if (!await blob.ExistsAsync())
                return null;

            var download = await blob.DownloadContentAsync();
            return download.Value.Content.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to local storage for byte read from {Container}/{Blob}", Sanitize(containerName), Sanitize(blobName));
            var fullPath = GetFallbackPath(containerName, blobName);
            return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath) : null;
        }
    }

    public async Task<bool> ExistsAsync(string containerName, string blobName)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            var blob = container.GetBlobClient(blobName);
            return await blob.ExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to local storage for exists check on {Container}/{Blob}", Sanitize(containerName), Sanitize(blobName));
            return File.Exists(GetFallbackPath(containerName, blobName));
        }
    }

    public async Task<IReadOnlyList<string>> ListBlobNamesAsync(string containerName)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            if (!await container.ExistsAsync())
                return [];

            var names = new List<string>();
            await foreach (var blobItem in container.GetBlobsAsync())
            {
                names.Add(blobItem.Name);
            }
            return names;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to local storage for blob listing of {Container}", Sanitize(containerName));
            var folder = Path.Combine(_fallbackRoot, containerName);
            if (!Directory.Exists(folder))
                return [];

            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
                .ToArray();
        }
    }

    public string GetBlobUrl(string containerName, string blobName)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        return container.GetBlobClient(blobName).Uri.ToString();
    }

    public async Task<string?> ReadLatestTextAsync(string containerName)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            Azure.Storage.Blobs.Models.BlobItem? latest = null;
            await foreach (var blobItem in container.GetBlobsAsync())
            {
                if (latest is null || blobItem.Properties.LastModified > latest.Properties.LastModified)
                {
                    latest = blobItem;
                }
            }

            if (latest == null)
                return null;

            var blob = container.GetBlobClient(latest.Name);
            var download = await blob.DownloadContentAsync();
            return download.Value.Content.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to local storage for latest read from {Container}", Sanitize(containerName));
            var folder = Path.Combine(_fallbackRoot, containerName);
            if (!Directory.Exists(folder))
                return null;

            var latestFile = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            return latestFile == null ? null : await File.ReadAllTextAsync(latestFile);
        }
    }

    private static string Sanitize(string value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');

    private string GetFallbackPath(string containerName, string blobName)
    {
        var sanitizedBlob = blobName.Replace('\\', '/');
        var relativePath = sanitizedBlob
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != "." && part != "..")
            .ToArray();
        var combined = Path.Combine([_fallbackRoot, containerName, .. relativePath]);
        var fullPath = Path.GetFullPath(combined);
        var containerRoot = Path.GetFullPath(Path.Combine(_fallbackRoot, containerName)) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(containerRoot, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid blob path.");

        return fullPath;
    }
}
