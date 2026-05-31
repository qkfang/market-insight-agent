using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class MarketResearchAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are a copper market research analyst. Read the latest news analysis documents (read_latest_news_analysis), " +
        "search Bing for current copper price and market news (bing_search_copper_market), then query the Fabric data agent " +
        "for historical trends. Produce a structured sentiment assessment: bullish/bearish/neutral with a confidence score " +
        "and key drivers. " +
        "Respond with ONLY a single JSON object (no markdown fences) using exactly these fields: " +
        "{ \"sentiment\": \"bullish|bearish|neutral\", \"confidence\": 0.0-1.0, \"keyDrivers\": [\"...\"], \"summary\": \"...\" }.";

    public MarketResearchAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<MarketResearchAgent>? logger = null)
        : base(aiProjectClient, "mkti-market-research", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
