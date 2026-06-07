using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class NewsAnalysisAgent : BaseAgent
{
    private const string AgentInstructions = """
        You are a news analysis agent for copper market. Read unprocessed news articles from the news-store (they are JSON objects) and store structured analysis in news-analysis.

        Step 1: Call extract_html_to_text_content to convert htmlContent to markdown and populate the textContent field for all articles in news-store.
        Step 2: Call analyze_news_json_to_analysis once to read the enriched articles (now with textContent) and write structured analysis to news-analysis.

        The analysis blob filename must match the source blob filename: {yyyyMMddHHmmssfff}_{guid}.json.
        Do not use Document Intelligence and do not convert HTML manually; the tools handle all extraction.

        Each article in news-store looks like this sample after step 1:
        {
          "id": "1",
          "guid": "3f3d8a16-355f-4a9e-a03b-cd744f6bf915",
          "title": "Copper Price Forecast: AI Demand and Tariff Risks Fuel Record Rally",
          "publishDate": "Wed, 03 Jun 2026 09:47:00 +0000",
          "publishDateIso": "2026-06-03T09:47:00+00:00",
          "description": "Copper Price Forecast: AI Demand and Tariff Risks Fuel Record Rally FXEmpire",
          "source": "FXEmpire",
          "domain": "fxempire.com",
          "originalUrl": "https://www.fxempire.com/forecasts/article/example",
          "htmlContent": "<!DOCTYPE html><html><body><h1>Copper Price Forecast</h1></body></html>",
          "textContent": "# Copper Price Forecast\n\nAI demand and tariff risks fuel record rally..."
        }

        """;

    public NewsAnalysisAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<NewsAnalysisAgent>? logger = null)
        : base(aiProjectClient, "mkti-news-analysis", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
