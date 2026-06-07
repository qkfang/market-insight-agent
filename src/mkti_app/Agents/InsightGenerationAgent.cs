using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class InsightGenerationAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are a market insight report writer. Follow these steps in order: " +
        "1. Call get_latest_research to get the most recent weekly market research JSON (the 'current' snapshot). Extract its weekStart date. " +
        "2. Call list_market_research_history with upToDate set to that weekStart date to retrieve all prior weekly research snapshots up to and including the current week, ordered oldest to newest. " +
        "3. Call read_news_analysis (or list_news_analysis) to gather the latest individual news analysis articles. " +
        "4. Using the current snapshot and all prior snapshots, produce a professional daily market insight report in markdown format with these sections: " +
        "   - Executive Summary " +
        "   - Current Market Sentiment (from the latest research: sentiment, confidence, key drivers, Bing news highlights) " +
        "   - Key Price Drivers " +
        "   - Risk Factors " +
        "   - Market View Timeline (a chronological table or narrative covering the past 6 months of weekly research snapshots — for each week include: week range, sentiment, confidence, key drivers, and a one-sentence summary of what drove the market view change from the prior week) " +
        "   - Outlook " +
        "   - Sources (cite news analysis articles and Bing results by title and date) " +
        "5. Store the result with today's date by calling the store_market_insight MCP tool, passing today's date (yyyy-MM-dd) and the markdown content.";

    public InsightGenerationAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<InsightGenerationAgent>? logger = null)
        : base(aiProjectClient, "mkti-insight-generation", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
