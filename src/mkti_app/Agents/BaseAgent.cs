using System.Diagnostics;
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
        var sw = Stopwatch.StartNew();
        var preview = message.Length > 120 ? message[..120] + "\u2026" : message;
        _logger.LogInformation("[{AgentId}] RunAsync starting. Message: {Preview}", _agentId, preview);

        var responseClient = await EnsureResponseClientAsync();

        CreateResponseOptions? nextOptions = new()
        {
            InputItems = { ResponseItem.CreateUserMessageItem(message) }
        };

        ResponseResult? result = null;
        var iteration = 0;

        while (nextOptions is not null)
        {
            iteration++;
            _logger.LogDebug("[{AgentId}] Calling model (iteration {Iteration})", _agentId, iteration);
            result = await responseClient.CreateResponseAsync(nextOptions);
            _logger.LogDebug("[{AgentId}] Response received (iteration {Iteration}), ResponseId={ResponseId}, OutputItems={Count}",
                _agentId, iteration, result?.Id, result?.OutputItems?.Count);
            nextOptions = null;

            foreach (var item in result.OutputItems)
            {
                if (item is McpToolCallApprovalRequestItem mcpCall)
                {
                    _logger.LogInformation("[{AgentId}] Auto-approving MCP tool call on {ServerLabel}", _agentId, mcpCall.ServerLabel);
                    nextOptions ??= new CreateResponseOptions { PreviousResponseId = result.Id };
                    nextOptions.InputItems.Add(ResponseItem.CreateMcpApprovalResponseItem(mcpCall.Id, approved: true));
                }
            }
        }

        var output = result?.GetOutputText() ?? string.Empty;
        sw.Stop();
        _logger.LogInformation("[{AgentId}] RunAsync completed in {ElapsedMs}ms, {Iterations} iteration(s), output {OutputLength} chars",
            _agentId, sw.ElapsedMilliseconds, iteration, output.Length);
        return output;
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
        _logger.LogDebug("[{AgentId}] StepAsync called, PreviousResponseId={PrevId}",
            _agentId, options.PreviousResponseId ?? "(none)");

        var responseClient = await EnsureResponseClientAsync();
        var sw = Stopwatch.StartNew();
        var clientResult = await responseClient.CreateResponseAsync(options);
        sw.Stop();
        var result = clientResult.Value;
        _logger.LogDebug("[{AgentId}] StepAsync response received in {ElapsedMs}ms, ResponseId={ResponseId}",
            _agentId, sw.ElapsedMilliseconds, result.Id);

        foreach (var item in result.OutputItems)
        {
            if (item is McpToolCallApprovalRequestItem mcpCall)
            {
                _logger.LogInformation("[{AgentId}] Awaiting user approval for MCP tool call on {ServerLabel}", _agentId, mcpCall.ServerLabel);
                return new AgentStepResult(null, new PendingToolApproval(result.Id, mcpCall.Id, mcpCall.ServerLabel), result.Id);
            }
        }

        var output = result.GetOutputText();
        _logger.LogInformation("[{AgentId}] StepAsync completed, output {OutputLength} chars", _agentId, output?.Length ?? 0);
        return new AgentStepResult(output, null, result.Id);
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

            _logger.LogInformation("[{AgentId}] Creating agent version for model '{DeploymentName}'", _agentId, _deploymentName);

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

            _logger.LogInformation("[{AgentId}] Agent version '{VersionName}' ready", _agentId, agentVersion.Value.Name);
            _responseClient = _aiProjectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(agentVersion.Value.Name);
            return _responseClient;
        }
        finally
        {
            _responseClientLock.Release();
        }
    }
}
