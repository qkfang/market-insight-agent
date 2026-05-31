using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class NewsIngestionAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are the News Ingestion Agent. Collect copper market news from trusted sources and store content with the MCP store_news tool.";

    public NewsIngestionAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<NewsIngestionAgent>? logger = null)
        : base(aiProjectClient, "mkti-news-ingestion", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
