using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class NewsAnalysisAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are the News Analysis Agent. Read ingested news, extract article facts and sentiment, and store parsed results to news-analysis via MCP tools.";

    public NewsAnalysisAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<NewsAnalysisAgent>? logger = null)
        : base(aiProjectClient, "mkti-news-analysis", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
