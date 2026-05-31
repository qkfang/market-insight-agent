using Azure.AI.Projects;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using mkti_app;
using mkti_app.Agents;
using mkti_app.Mcp;
using mkti_app.Services;
using OpenAI.Responses;
using OpenTelemetry.Instrumentation.Http;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:5001");
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
    builder.Services.Configure<HttpClientTraceInstrumentationOptions>(options =>
    {
        options.FilterHttpRequestMessage = req =>
        {
            var host = req.RequestUri?.Host;
            return string.IsNullOrEmpty(host) || !host.EndsWith("livediagnostics.monitor.azure.com", StringComparison.OrdinalIgnoreCase);
        };
    });
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();
builder.Services.AddCors();

var projectEndpoint = builder.Configuration["AZURE_AI_PROJECT_ENDPOINT"] ?? "https://example.invalid";
var deploymentName = builder.Configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"] ?? "gpt-4.1-mini";
var appMcpUrl = builder.Configuration["APP_MCP_URL"] ?? "http://localhost:5001";
var storageAccountName = builder.Configuration["AZURE_STORAGE_ACCOUNT_NAME"] ?? "devstoreaccount1";
var docIntelligenceEndpoint = builder.Configuration["AZURE_DOC_INTELLIGENCE_ENDPOINT"] ?? string.Empty;
var fabricWorkspaceId = builder.Configuration["FABRIC_LAKEHOUSE_WORKSPACE_ID"] ?? string.Empty;
var fabricLakehouseId = builder.Configuration["FABRIC_LAKEHOUSE_ID"] ?? string.Empty;

var credential = new DefaultAzureCredential();

builder.Services.AddSingleton(sp => new BlobStorageService(
    storageAccountName,
    credential,
    sp.GetRequiredService<ILogger<BlobStorageService>>()));

builder.Services.AddSingleton(sp => new DocIntelligenceService(
    docIntelligenceEndpoint,
    credential,
    sp.GetRequiredService<ILogger<DocIntelligenceService>>()));

builder.Services.AddSingleton(sp => new FabricLakehouseService(
    fabricWorkspaceId,
    fabricLakehouseId,
    credential,
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<ILogger<FabricLakehouseService>>()));

builder.Services.AddMcpServer()
    .WithHttpTransport(options => { options.Stateless = true; })
    .WithTools<MarketInsightMcpTools>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp"))
    {
        var accept = context.Request.Headers.Accept.ToString();
        if (string.IsNullOrEmpty(accept) || !accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.Headers.Accept = "application/json, text/event-stream";
        }
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapMcp("/mcp");
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Redirect("/index.html"));

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var aiProjectClient = new AIProjectClient(new Uri(projectEndpoint), credential);
var appMcpTool = ResponseTool.CreateMcpTool(
    serverLabel: "market-insight-mcp",
    serverUri: new Uri($"{appMcpUrl.TrimEnd('/')}/mcp"),
    toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval));

var newsIngestionAgent = new NewsIngestionAgent(aiProjectClient, deploymentName, [appMcpTool], loggerFactory.CreateLogger<NewsIngestionAgent>());
var newsAnalysisAgent = new NewsAnalysisAgent(aiProjectClient, deploymentName, [appMcpTool], loggerFactory.CreateLogger<NewsAnalysisAgent>());
var marketResearchAgent = new MarketResearchAgent(aiProjectClient, deploymentName, [appMcpTool], loggerFactory.CreateLogger<MarketResearchAgent>());
var insightGenerationAgent = new InsightGenerationAgent(aiProjectClient, deploymentName, [appMcpTool], loggerFactory.CreateLogger<InsightGenerationAgent>());

var blobStorageService = app.Services.GetRequiredService<BlobStorageService>();
var fabricLakehouseService = app.Services.GetRequiredService<FabricLakehouseService>();

app.MapAllEndpoints(
    newsIngestionAgent,
    newsAnalysisAgent,
    marketResearchAgent,
    insightGenerationAgent,
    blobStorageService,
    fabricLakehouseService);

await app.RunAsync();
