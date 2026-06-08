# Market Insight Agent — Solution Design

## Overview

Market Insight Agent (`mkti_app`) is an ASP.NET Core web application that orchestrates a pipeline of AI agents to ingest commodity news, analyse it, produce weekly research snapshots, generate professional market-insight reports, and deliver branded PDF subscriptions — all powered by Azure AI Foundry, Azure Blob Storage, Microsoft Fabric Lakehouse, and Bing Search.

---

## High-Level Architecture

```mermaid
graph TB
    subgraph Browser["Browser (wwwroot SPA)"]
        UI_Ingest[Ingest]
        UI_Analyze[Analyse]
        UI_Research[Research]
        UI_Generate[Generate]
        UI_Delivery[Delivery]
        UI_Knowledge[Knowledge]
    end

    subgraph App["ASP.NET Core — mkti_app"]
        API[REST API<br/>/api/*]
        MCP[MCP Server<br/>/mcp]

        subgraph Agents["AI Agents (Azure AI Foundry)"]
            A1[NewsIngestionAgent]
            A2[NewsAnalysisAgent]
            A3[MarketResearchAgent]
            A4[InsightGenerationAgent]
            A5[SubscriptionAgent]
        end

        subgraph Services["Backend Services"]
            S1[BlobStorageService]
            S2[BingSearchService]
            S3[DocIntelligenceService]
            S4[FabricLakehouseService]
        end
    end

    subgraph Azure["Azure Infrastructure"]
        AI_Foundry[Azure AI Foundry<br/>GPT-5.4]
        Blob[Azure Blob Storage]
        APPI[Application Insights]
        Bing[Bing Search v7]
        DocIntel[Document Intelligence]
    end

    subgraph Fabric["Microsoft Fabric"]
        Lakehouse[Fabric Lakehouse]
        FabricMCP[Fabric MCP Server]
    end

    Browser -- HTTP --> API
    API --> Agents
    Agents -- tool calls --> MCP
    MCP --> Services

    A1 & A2 & A3 & A4 & A5 -- Azure AI Responses API --> AI_Foundry
    A3 -- Fabric MCP --> FabricMCP

    S1 --> Blob
    S2 --> Bing
    S3 --> DocIntel
    S4 --> Lakehouse
    App --> APPI
```

---

## Agent Pipeline (Data Flow)

```mermaid
flowchart LR
    subgraph Source["Source Data"]
        Files["data/articles/\nyyyy-MM-dd_guid.json"]
        BingAPI["Bing Search API"]
    end

    subgraph Pipeline["Agent Pipeline"]
        direction TB
        N1["① NewsIngestionAgent\nIngest article JSON files"]
        N2["② NewsAnalysisAgent\nHTML→text + structured analysis"]
        N3["③ MarketResearchAgent\nWeekly research report"]
        N4["④ InsightGenerationAgent\nProfessional insight report\n(6-month history)"]
        N5["⑤ SubscriptionAgent\nBranded HTML + PDF delivery"]
    end

    subgraph Storage["Azure Blob Storage"]
        C1["news-store\n{ts}_{guid}.json"]
        C2["news-analysis\n{ts}_{guid}.json"]
        C3["market-research\n{market}_{week}.json"]
        C4["market-insight\n{market}_{date}.md"]
        C5["temp/ reports\n*.html  *.pdf"]
    end

    Files -->|ingest_articles_json_to_news_store| N1
    N1 --> C1
    C1 -->|extract_html_to_text_content\nanalyze_news_json_to_analysis| N2
    N2 --> C2
    C2 -->|read_news_analysis_by_market| N3
    BingAPI -->|bing_search_market| N3
    N3 --> C3
    C3 -->|list_market_research_history| N4
    C2 -->|read_news_analysis| N4
    N4 --> C4
    C4 -->|read_market_insight_for_market| N5
    N5 --> C5
```

---

## Component Detail

```mermaid
classDiagram
    class mkti_app {
        +MapAllEndpoints()
        +MapMcp("/mcp")
    }

    class BaseAgent {
        #AIProjectClient aiProjectClient
        #string deploymentName
        +RunAsync(message) Task~string~
    }

    class NewsIngestionAgent {
        +AgentId: mkti-news-ingestion
        +RunAsync(dateArgs) Task~string~
    }
    class NewsAnalysisAgent {
        +AgentId: mkti-news-analysis
        +RunAsync(args) Task~string~
    }
    class MarketResearchAgent {
        +AgentId: mkti-market-research
        +RunAsync(args) Task~string~
    }
    class InsightGenerationAgent {
        +AgentId: mkti-insight-generation
        +RunAsync(args) Task~string~
    }
    class SubscriptionAgent {
        +AgentId: mkti-subscription
        +RunAsync(args) Task~string~
    }

    class MarketInsightMcpTools {
        +ingest_articles_json_to_news_store()
        +extract_html_to_text_content()
        +analyze_news_json_to_analysis()
        +read_news_analysis_by_market()
        +store_weekly_market_research()
        +list_market_research_history()
        +read_market_insight_for_market()
        +store_market_insight_for_market()
        +generate_subscription_report()
        +generate_pdf_report()
        +bing_search_market()
    }

    class BlobStorageService {
        +WriteTextAsync()
        +ReadTextAsync()
        +ListBlobNamesAsync()
    }
    class BingSearchService {
        +SearchAsync()
    }
    class DocIntelligenceService {
        +ExtractTextAsync()
    }
    class FabricLakehouseService {
        +WriteFileAsync()
        +ReadFileAsync()
    }

    BaseAgent <|-- NewsIngestionAgent
    BaseAgent <|-- NewsAnalysisAgent
    BaseAgent <|-- MarketResearchAgent
    BaseAgent <|-- InsightGenerationAgent
    BaseAgent <|-- SubscriptionAgent

    mkti_app --> NewsIngestionAgent
    mkti_app --> NewsAnalysisAgent
    mkti_app --> MarketResearchAgent
    mkti_app --> InsightGenerationAgent
    mkti_app --> SubscriptionAgent
    mkti_app --> MarketInsightMcpTools

    MarketInsightMcpTools --> BlobStorageService
    MarketInsightMcpTools --> BingSearchService
    MarketInsightMcpTools --> DocIntelligenceService
    MarketInsightMcpTools --> FabricLakehouseService
```

---

## Azure Infrastructure

```mermaid
graph TB
    subgraph rg["Resource Group  rg-market-insight"]
        subgraph monitoring["Monitoring"]
            LAW[Log Analytics Workspace\nbaseName-law]
            APPI[Application Insights\nbaseName-appi]
        end
        subgraph compute["Compute"]
            ASP[App Service Plan S1\nbaseName-plan]
            WEB[Web App\nbaseName-web]
        end
        subgraph ai["AI"]
            AIS[Azure AI Services\nbaseName-ais]
            PROJ[AI Foundry Project\nbaseName-proj]
            BING[Bing Search v7\nbaseName-bing]
        end
        subgraph storage["Storage"]
            SA[Storage Account\nbaseNamesa]
            SA_NS[Container: news-store]
            SA_NA[Container: news-analysis]
            SA_MR[Container: market-research]
            SA_MI[Container: market-insight]
        end
    end

    subgraph fabric_plane["Microsoft Fabric (F2 capacity)"]
        FC[Fabric Capacity\nbaseNamefabric]
        FL[Fabric Lakehouse]
    end

    WEB --> AIS
    WEB --> PROJ
    WEB --> SA
    WEB --> BING
    WEB --> FL
    PROJ --> AIS
    SA --> SA_NS & SA_NA & SA_MR & SA_MI
    APPI --> LAW
    WEB --> APPI
    FC --> FL
```

---

## API Endpoints

| Method | Path | Agent / Handler | Description |
|--------|------|----------------|-------------|
| GET | `/api/news/ingest` | NewsIngestionAgent | Ingest article JSON files into news-store |
| GET | `/api/news/list` | BlobStorageService | List blobs in news-store |
| GET | `/api/articles/list` | File system | List local article JSON files |
| GET | `/api/news/analyze` | NewsAnalysisAgent | Analyse news-store articles into news-analysis |
| GET | `/api/news/analysis/list` | BlobStorageService | List blobs in news-analysis |
| GET | `/api/research` | MarketResearchAgent | Run weekly market research |
| GET | `/api/research/list` | BlobStorageService | List market-research blobs |
| GET | `/api/generate` | InsightGenerationAgent | Generate market insight report |
| GET | `/api/generate/list` | BlobStorageService | List market-insight blobs |
| GET | `/api/subscription/deliver` | SubscriptionAgent | Generate subscription HTML + PDF |
| GET | `/api/knowledge` | BlobStorageService | Knowledge base query |
| GET | `/health` | ASP.NET Health Checks | Health probe |
| ANY | `/mcp` | MCP Server | Model Context Protocol endpoint |

---

## Sequence: End-to-End Pipeline

```mermaid
sequenceDiagram
    actor User
    participant UI as Browser UI
    participant API as REST API
    participant Agent as AI Agent
    participant Foundry as Azure AI Foundry
    participant MCP as MCP Server
    participant Blob as Azure Blob Storage
    participant Bing as Bing Search

    User->>UI: Open Ingest tab, click Ingest
    UI->>API: GET /api/news/ingest?from=&to=
    API->>Agent: NewsIngestionAgent.RunAsync()
    Agent->>Foundry: Create thread + run (GPT-5.4)
    Foundry-->>Agent: tool_call: ingest_articles_json_to_news_store
    Agent->>MCP: ingest_articles_json_to_news_store(dateFrom, dateTo)
    MCP->>Blob: Upload JSONs → news-store
    MCP-->>Agent: {ingested: N}
    Agent-->>API: result
    API-->>UI: {articlesStored: N}

    User->>UI: Open Analyse tab, click Analyse
    UI->>API: GET /api/news/analyze
    API->>Agent: NewsAnalysisAgent.RunAsync()
    Agent->>Foundry: Create thread + run
    Foundry-->>Agent: tool_call: extract_html_to_text_content
    Agent->>MCP: extract_html_to_text_content()
    MCP->>Blob: Read news-store, write textContent
    Foundry-->>Agent: tool_call: analyze_news_json_to_analysis
    Agent->>MCP: analyze_news_json_to_analysis()
    MCP->>Blob: Write → news-analysis

    User->>UI: Open Research tab, click Research
    UI->>API: GET /api/research?market=copper&weekStart=&weekEnd=
    API->>Agent: MarketResearchAgent.RunAsync()
    Agent->>Foundry: Create thread + run
    Foundry-->>Agent: tool_call: read_news_analysis_by_market
    Agent->>MCP: read_news_analysis_by_market()
    MCP->>Blob: Read news-analysis
    Foundry-->>Agent: tool_call: bing_search_market
    Agent->>MCP: bing_search_market()
    MCP->>Bing: Search query
    Bing-->>MCP: results
    Foundry-->>Agent: tool_call: store_weekly_market_research
    Agent->>MCP: store_weekly_market_research(report JSON)
    MCP->>Blob: Write → market-research

    User->>UI: Open Generate tab, click Generate
    UI->>API: GET /api/generate?market=copper
    API->>Agent: InsightGenerationAgent.RunAsync()
    Agent->>Foundry: Create thread + run
    Foundry-->>Agent: list_market_research_history + read_news_analysis
    Agent->>MCP: read history + analysis
    MCP->>Blob: Read market-research + news-analysis
    Foundry-->>Agent: tool_call: store_market_insight_for_market
    Agent->>MCP: store_market_insight_for_market(markdown)
    MCP->>Blob: Write → market-insight

    User->>UI: Open Delivery tab, click Deliver
    UI->>API: GET /api/subscription/deliver
    API->>Agent: SubscriptionAgent.RunAsync()
    Agent->>Foundry: Create thread + run
    Foundry-->>Agent: read_market_insight_for_market
    Agent->>MCP: read latest insight
    MCP->>Blob: Read market-insight
    Foundry-->>Agent: generate_subscription_report + generate_pdf_report
    Agent->>MCP: Generate HTML + PDF
    MCP-->>Agent: {filename, pdfUrl}
    Agent-->>API: report links
    API-->>UI: {htmlBase64, pdfUrl}
```
