using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core;

namespace mkti_app.Services;

public record DocIntelligenceResult(string Markdown);

public sealed class DocIntelligenceService
{
    private readonly DocumentIntelligenceClient? _client;
    private readonly ILogger<DocIntelligenceService> _logger;
    private readonly string _modelId;

    public DocIntelligenceService(string endpoint, TokenCredential credential, ILogger<DocIntelligenceService> logger, string modelId = "prebuilt-read")
    {
        _logger = logger;
        _modelId = string.IsNullOrWhiteSpace(modelId) ? "prebuilt-read" : modelId;

        if (!string.IsNullOrWhiteSpace(endpoint) && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            _client = new DocumentIntelligenceClient(endpointUri, credential);
        }
        else
        {
            _logger.LogWarning("AZURE_DOC_INTELLIGENCE_ENDPOINT is not configured; Document Intelligence is disabled.");
        }
    }

    public bool IsConfigured => _client is not null;

    public async Task<DocIntelligenceResult> AnalyzeFromBytesAsync(BinaryData content)
    {
        if (_client is null)
            throw new InvalidOperationException("Document Intelligence endpoint is not configured (AZURE_DOC_INTELLIGENCE_ENDPOINT).");

        _logger.LogInformation("Analyzing document ({Bytes} bytes) with model {Model}", content.ToMemory().Length, _modelId);
        var options = new AnalyzeDocumentOptions(_modelId, content)
        {
            OutputContentFormat = DocumentContentFormat.Markdown
        };
        Operation<AnalyzeResult> operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, options);
        return new DocIntelligenceResult(operation.Value.Content ?? string.Empty);
    }
}
