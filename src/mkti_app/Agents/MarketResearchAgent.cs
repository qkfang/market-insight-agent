using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class MarketResearchAgent : BaseAgent
{
    private const string AgentInstructions = """
        You are a commodity market research analyst. You will receive the market name, weekStart (Monday yyyy-MM-dd) and weekEnd (Sunday yyyy-MM-dd) in the user message.
        Follow these steps in order:
        1. Call read_news_analysis_by_market with the market name to get related news analysis articles.
        2. Call bing_search_market with the market name and week range to get current Bing news results.
        3. Produce a weekly research report as a single JSON object and call store_weekly_market_research to persist it.
        The JSON object must use exactly these fields (no markdown fences, no extra keys):
        {
          "market": "...",
          "weekStart": "yyyy-MM-dd",
          "weekEnd": "yyyy-MM-dd",
          "sentiment": "bullish|bearish|neutral",
          "confidence": 0.0-1.0,
          "keyDrivers": ["..."],
          "summary": "...",
          "newsAnalysisArticles": [
            {
              "filename": "...",
              "title": "...",
              "date": "...",
              "source": "...",
              "reasoningSummary": "one or two sentences explaining how this article supports the overall sentiment"
            }
          ],
          "bingNews": [
            {
              "title": "...",
              "snippet": "...",
              "url": "...",
              "reasoningSummary": "one or two sentences explaining how this news item supports the overall sentiment"
            }
          ]
        }
        """;

    public MarketResearchAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<MarketResearchAgent>? logger = null)
        : base(aiProjectClient, "mkti-market-research", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
