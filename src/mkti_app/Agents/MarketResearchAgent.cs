using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class MarketResearchAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are the Market Research Agent. Evaluate current copper market conditions and produce concise research findings with risk and sentiment changes.";

    public MarketResearchAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<MarketResearchAgent>? logger = null)
        : base(aiProjectClient, "mkti-market-research", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
