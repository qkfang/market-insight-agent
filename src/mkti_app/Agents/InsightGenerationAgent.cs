using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class InsightGenerationAgent : BaseAgent
{
    private const string AgentInstructions = """
        You are a market insight report writer. Follow these steps in order:
        1. Call get_latest_research to get the most recent weekly market research JSON (the 'current' snapshot). Extract its weekStart date.
        2. Call list_market_research_history with upToDate set to that weekStart date to retrieve all prior weekly research snapshots up to and including the current week, ordered oldest to newest.
        3. Call read_news_analysis (or list_news_analysis) to gather the latest individual news analysis articles.
        4. Using the current snapshot and all prior snapshots, produce a professional daily market insight report in markdown format with these sections:
           - Executive Summary
           - Current Market Sentiment (from the latest research: sentiment, confidence, key drivers, Bing news highlights)
           - Key Price Drivers
           - Risk Factors
           - Market View Timeline (a chronological table or narrative covering the past 6 months of weekly research snapshots — for each week include: week range, sentiment, confidence, key drivers, and a one-sentence summary of what drove the market view change from the prior week)
           - Outlook
           - Sources (cite news analysis articles and Bing results by title and date)
        5. Store the markdown report with today's date by calling the store_market_insight MCP tool, passing today's date (yyyy-MM-dd) and the markdown content.
        6. Also produce a structured JSON report using exactly this schema and store it by calling store_market_insight_json, passing today's date (yyyy-MM-dd) and the JSON string:
           {
             "reportDate": "2025-01-15",
             "weekStart": "2025-01-13",
             "executiveSummary": "One-paragraph summary of the overall market outlook.",
             "currentMarketSentiment": {
               "sentiment": "bullish|bearish|neutral",
               "confidence": 0.85,
               "keyDrivers": ["Driver one", "Driver two"],
               "bingNewsHighlights": ["Headline one", "Headline two"]
             },
             "keyPriceDrivers": ["Factor one", "Factor two"],
             "riskFactors": ["Risk one", "Risk two"],
             "marketViewTimeline": [
               {
                 "weekStart": "2024-07-15",
                 "weekEnd": "2024-07-21",
                 "sentiment": "neutral",
                 "confidence": 0.72,
                 "keyDrivers": ["Driver one"],
                 "changeSummary": "One sentence describing what drove the sentiment change from the prior week."
               }
             ],
             "outlook": "Forward-looking paragraph summarising expected near-term direction.",
             "sources": [
               {
                 "title": "Article or report title",
                 "date": "2025-01-14",
                 "url": "https://example.com/article"
               }
             ]
           }
        """;

    public InsightGenerationAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<InsightGenerationAgent>? logger = null)
        : base(aiProjectClient, "mkti-insight-generation", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
