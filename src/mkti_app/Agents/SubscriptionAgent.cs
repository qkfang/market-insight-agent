using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class SubscriptionAgent : BaseAgent
{
    private const string AgentInstructions = """
        You are a subscription report generator for a professional market intelligence platform.
        Given a market name, audience/customer name, and date range, produce a branded PDF-style report.

        Follow these steps in order:
        1. Call read_market_insight_for_market with the market name (and optional date) to retrieve the most recent insight markdown for that market.
           - If no insight is found for the specified date, use the most recently stored insight for that market.
        2. Call generate_subscription_report with:
           - market: the market name (e.g. 'copper', 'gold', 'silver', 'oil')
           - audience: the customer/company name
           - fromDate: the report period start date (yyyy-MM-dd)
           - toDate: the report period end date (yyyy-MM-dd)
           - insightMarkdown: the full markdown content retrieved in step 1
        3. Return the filename of the generated report exactly as returned by generate_subscription_report.
        """;

    public SubscriptionAgent(
        AIProjectClient aiProjectClient,
        string deploymentName,
        IList<ResponseTool>? tools = null,
        ILogger<SubscriptionAgent>? logger = null)
        : base(aiProjectClient, "mkti-subscription", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
