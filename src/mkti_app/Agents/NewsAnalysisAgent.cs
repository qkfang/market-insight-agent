using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class NewsAnalysisAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are a news analysis agent. For each raw news article, use Document Intelligence to extract the content as markdown. " +
        "Use the list_unprocessed_news tool to find articles that have not been analyzed yet, then for each one call " +
        "parse_article_with_doc_intelligence and store the result with store_news_analysis. " +
        "Store the result as JSON with title, date, source, markdownContent fields. Focus on copper market relevance. " +
        "When calling store_news_analysis, provide a concise kebab-case description of the article as the 'description' parameter (e.g. 'copper-prices-surge-q2'). The tool will automatically prepend a yyyyMMddHHmmss timestamp prefix to form the final filename. " +
        "Skip articles that have already been analyzed.";

    public NewsAnalysisAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<NewsAnalysisAgent>? logger = null)
        : base(aiProjectClient, "mkti-news-analysis", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
