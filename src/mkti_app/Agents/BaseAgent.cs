using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace mkti_app.Agents;

public record PendingToolApproval(string ResponseId, string ApprovalItemId, string ServerLabel);

public record AgentStepResult(string? Result, PendingToolApproval? Pending, string? ResponseId);

public abstract class BaseAgent
{
    private readonly AIProjectClient _aiProjectClient;
    private readonly string _deploymentName;
    private readonly IList<ResponseTool> _tools;
    private readonly SemaphoreSlim _responseClientLock = new(1, 1);
    private ProjectResponsesClient? _responseClient;

    protected readonly ILogger _logger;
    protected readonly string _agentId;

    public string AgentId => _agentId;
    public string Instructions { get; }

    protected BaseAgent(
        AIProjectClient aiProjectClient,
        string agentId,
        string deploymentName,
        string instructions,
        IList<ResponseTool>? tools = null,
        ILogger? logger = null)
    {
        _aiProjectClient = aiProjectClient;
        _deploymentName = deploymentName;
        _agentId = agentId;
        Instructions = instructions;
        _tools = tools ?? [];
        _logger = logger ?? LoggerFactory.Create(b => b.AddConsole()).CreateLogger(agentId);
    }

    public async Task<string> RunAsync(string message)
    {
        var responseClient = await EnsureResponseClientAsync();

        CreateResponseOptions? nextOptions = new()
        {
            InputItems = { ResponseItem.CreateUserMessageItem(message) }
        };

        ResponseResult? result = null;

        while (nextOptions is not null)
        {
            result = await responseClient.CreateResponseAsync(nextOptions);
            nextOptions = null;

            foreach (var item in result.OutputItems)
            {
                if (item is McpToolCallApprovalRequestItem mcpCall)
                {
                    _logger.LogInformation("Auto-approving MCP tool call on {ServerLabel}", mcpCall.ServerLabel);
                    nextOptions ??= new CreateResponseOptions { PreviousResponseId = result.Id };
                    nextOptions.InputItems.Add(ResponseItem.CreateMcpApprovalResponseItem(mcpCall.Id, approved: true));
                }
            }
        }

        return result?.GetOutputText() ?? string.Empty;
    }

    public Task<AgentStepResult> StartRunAsync(string message)
    {
        var options = new CreateResponseOptions
        {
            InputItems = { ResponseItem.CreateUserMessageItem(message) }
        };
        return StepAsync(options);
    }

    public Task<AgentStepResult> ChatAsync(string previousResponseId, string message)
    {
        var options = new CreateResponseOptions
        {
            PreviousResponseId = previousResponseId,
            InputItems = { ResponseItem.CreateUserMessageItem(message) }
        };
        return StepAsync(options);
    }

    public Task<AgentStepResult> ContinueRunAsync(string previousResponseId, string approvalItemId, bool approved)
    {
        var options = new CreateResponseOptions
        {
            PreviousResponseId = previousResponseId,
            InputItems = { ResponseItem.CreateMcpApprovalResponseItem(approvalItemId, approved) }
        };
        return StepAsync(options);
    }

    private async Task<AgentStepResult> StepAsync(CreateResponseOptions options)
    {
        var responseClient = await EnsureResponseClientAsync();
        var clientResult = await responseClient.CreateResponseAsync(options);
        var result = clientResult.Value;

        foreach (var item in result.OutputItems)
        {
            if (item is McpToolCallApprovalRequestItem mcpCall)
            {
                _logger.LogInformation("Awaiting user approval for MCP tool call on {ServerLabel}", mcpCall.ServerLabel);
                return new AgentStepResult(null, new PendingToolApproval(result.Id, mcpCall.Id, mcpCall.ServerLabel), result.Id);
            }
        }

        return new AgentStepResult(result.GetOutputText(), null, result.Id);
    }

    private async Task<ProjectResponsesClient> EnsureResponseClientAsync()
    {
        if (_responseClient is not null)
            return _responseClient;

        await _responseClientLock.WaitAsync();
        try
        {
            if (_responseClient is not null)
                return _responseClient;

            var agentDefinition = new DeclarativeAgentDefinition(model: _deploymentName)
            {
                Instructions = Instructions
            };

            foreach (var tool in _tools)
            {
                agentDefinition.Tools.Add(tool);
            }

            var agentVersion = await _aiProjectClient.AgentAdministrationClient.CreateAgentVersionAsync(
                _agentId,
                new ProjectsAgentVersionCreationOptions(agentDefinition));

            _responseClient = _aiProjectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(agentVersion.Value.Name);
            return _responseClient;
        }
        finally
        {
            _responseClientLock.Release();
        }
    }
}
