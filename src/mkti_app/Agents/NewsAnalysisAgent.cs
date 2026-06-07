using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class NewsAnalysisAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are a news analysis agent for copper market. Read unprocessed news articles from the news-store (they are JSON objects) and store structured analysis in news-analysis. " +
        "Use the analyze_news_json_to_analysis tool once per run. " +
        "The analysis blob filename must match the source blob filename: {yyyyMMddHHmmssfff}_{guid}.json. " +
        "Do not use Document Intelligence and do not convert HTML manually; the tool handles extraction from JSON fields.";

    public NewsAnalysisAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<NewsAnalysisAgent>? logger = null)
        : base(aiProjectClient, "mkti-news-analysis", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
