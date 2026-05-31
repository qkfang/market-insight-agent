using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class InsightGenerationAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are the Insight Generation Agent. Generate a daily copper market insight paper in markdown and store it using the store_insight MCP tool.";

    public InsightGenerationAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<InsightGenerationAgent>? logger = null)
        : base(aiProjectClient, "mkti-insight-generation", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
