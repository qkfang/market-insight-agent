using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class InsightGenerationAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are a market insight report writer. First call get_latest_research and read_news_analysis (or list_news_analysis) to gather the copper market sentiment research and latest news analysis. " +
        "Using that information, produce a professional daily market insight report in markdown format. " +
        "Include these sections: Executive Summary, Market Sentiment, Key Price Drivers, Risk Factors, Outlook, and Sources. " +
        "Store the result with today's date by calling the store_market_insight MCP tool, passing today's date (yyyy-MM-dd) and the markdown content.";

    public InsightGenerationAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<InsightGenerationAgent>? logger = null)
        : base(aiProjectClient, "mkti-insight-generation", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
