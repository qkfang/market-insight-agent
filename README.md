# market-insight-agent

# general

project_name=market_insight
project_name_abbr=mkti

# infra

create foundry
create web app and app service plan use s1 sku
create app insight and storage account
update bicep folder to align with resources
add required param inside main.biceparam
adjust deploy.ps1 for running the deployment locally (keep hard coded values inline)
create fabric capacility, F2

# web app

create .net C# web app with frontend, should be .net 10
foundry agent integration following Agents folder
expose mcp tools for integration using SDK in Program.cs
all APIs on the web app should be in Apis.cs file

# business flow

the goal of the project is to prepare market insight summary for copper market.

- page 1: news ingestion: download news from rss feed and land in fabric datalake: news_store folder as pdf or html
- page 2: news analysis: use document intelligence to parse article into markdown: news_analysis as json
- page 3: market research: query the lates doc in the news archive to detect sentiment change for coppoer market: using bing search to research the specific market via fabric data agent via MCP
- page 4: insight generation: produce a researh paper for daily market insight and store in fabric: market_insight folder
- page 5: insight subscription: user can pick which markets & item to subscribe
- page 6: insight delivery: the related market_insight document is displayed on web page every morning.

# pattern

Agent using Foundry: https://github.com/qkfang/invoice-ledger-agent/blob/main/src/invledger_app/Agents/BaseAgent.cs
.Net MCP: https://github.com/qkfang/invoice-ledger-agent/blob/main/src/invledger_app/Program.cs

Fabric Data Agent MCP: https://github.com/qkfang/forex-trading-agent/blob/main/src/agent-forex/Program.cs

Microsoft Agent Framework: MAF documentation here: https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/02-agents/AgentsWithFoundry

foundry sdk examples: https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/ai/Azure.AI.Extensions.OpenAI/samples

Document Intelligence: https://github.com/qkfang/invoice-ledger-agent/blob/main/src/invledger_app/Mcp/InvLedgerMcpTools.cs

# prompt

this is the project to be developped. Try to split the development into a few tickets and they can be done in parallel by github coding agents @Copilot.

if there is dependencies of each ticket, try to split into phases in title so that we can continue building once first ticekt is done. e.g. P1.a, P1.b, then P2.a, P2.b etc.
