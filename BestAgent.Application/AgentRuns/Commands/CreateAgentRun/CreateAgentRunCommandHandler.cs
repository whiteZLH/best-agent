using BestAgent.Application.AgentDefinitions;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CreateAgentRun;

public class CreateAgentRunCommandHandler : IRequestHandler<CreateAgentRunCommand, CreateAgentRunResult>
{
    private const string RuntimeInstruction =
        """
        Return JSON only.
        Use {"action":"respond","response":"..."} for a final answer.
        Use {"action":"tool_call","toolName":"...","toolInput":"..."} to call one tool.
        """;

    private readonly IAgentDefinitionRepository _agentDefinitionRepository;
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IModelGateway _modelGateway;
    private readonly IStepDecisionParser _stepDecisionParser;
    private readonly IToolExecutor _toolExecutor;

    public CreateAgentRunCommandHandler(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IModelGateway modelGateway,
        IStepDecisionParser stepDecisionParser,
        IToolExecutor toolExecutor)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _modelGateway = modelGateway;
        _stepDecisionParser = stepDecisionParser;
        _toolExecutor = toolExecutor;
    }

    public async Task<CreateAgentRunResult> Handle(CreateAgentRunCommand request, CancellationToken cancellationToken)
    {
        var resolvedDefinition = await _agentDefinitionRepository.GetEnabledByCodeAsync(request.AgentCode, cancellationToken);
        if (resolvedDefinition is null)
            throw new NotFoundException("AgentDefinition", request.AgentCode);

        var now = DateTime.UtcNow;
        var runId = Guid.NewGuid().ToString("N");
        var agentRun = new AgentRun
        {
            RunId = runId,
            AgentCode = request.AgentCode,
            AgentDefinitionVersionId = resolvedDefinition.Version.Id,
            Status = "Running",
            InputPayload = request.Input,
            RootRunId = runId,
            IdempotencyKey = runId,
            MaxTurns = resolvedDefinition.Version.MaxTurns,
            MaxCost = resolvedDefinition.Version.MaxCost,
            StartedAt = now,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        await _agentRunRepository.AddAsync(agentRun, cancellationToken);
        await _agentStepRepository.AddAsync(CreateStep(runId, 1, "created", "Completed", request.Input, null, null, now, now), cancellationToken);
        await _agentStepRepository.AddAsync(CreateStep(runId, 2, "running", "Completed", request.Input, null, null, now, now), cancellationToken);

        var nextStepNo = 3;
        var currentInput = request.Input;

        try
        {
            for (var turn = 0; turn < agentRun.MaxTurns; turn++)
            {
                var decisionResult = await GenerateDecisionAsync(resolvedDefinition.Version, currentInput, cancellationToken);
                await _agentStepRepository.AddAsync(CreateStep(
                    runId, nextStepNo++, "model_call", "Completed",
                    currentInput, decisionResult.RawOutput, null,
                    decisionResult.StartedAt, decisionResult.EndedAt), cancellationToken);

                if (string.Equals(decisionResult.Decision.Action, "respond", StringComparison.OrdinalIgnoreCase))
                {
                    var output = GetResponse(decisionResult.Decision);
                    return await CompleteRun(agentRun, nextStepNo, request.Input, output, cancellationToken);
                }

                if (!string.Equals(decisionResult.Decision.Action, "tool_call", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Unsupported decision action '{decisionResult.Decision.Action}'.");

                var toolName = decisionResult.Decision.ToolName!;
                EnsureToolAllowed(toolName, resolvedDefinition, request.AgentCode);

                var toolStartedAt = DateTime.UtcNow;
                var toolResult = await _toolExecutor.ExecuteAsync(
                    toolName,
                    decisionResult.Decision.ToolInput,
                    new ToolExecutionContext(runId, request.AgentCode, request.Input),
                    cancellationToken);
                var toolEndedAt = DateTime.UtcNow;

                await _agentStepRepository.AddAsync(CreateStep(
                    runId, nextStepNo++, "tool_call", "Completed",
                    decisionResult.Decision.ToolInput, toolResult.Output, null,
                    toolStartedAt, toolEndedAt), cancellationToken);

                currentInput = BuildToolFollowUpInput(request.Input, toolResult);
            }

            throw new InvalidOperationException("Max turns exceeded.");
        }
        catch (Exception ex)
        {
            var failedAt = DateTime.UtcNow;
            var error = ex.Message[..Math.Min(ex.Message.Length, 256)];
            agentRun = agentRun with
            {
                Status = "Failed",
                InterruptReason = error,
                CurrentStepNo = nextStepNo,
                EndedAt = failedAt,
                LastModifyTime = failedAt
            };

            await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
            await _agentStepRepository.AddAsync(CreateStep(
                runId, nextStepNo, "failed", "Failed",
                request.Input, null, error, failedAt, failedAt), cancellationToken);
            throw;
        }
    }

    private async Task<CreateAgentRunResult> CompleteRun(
        AgentRun agentRun, int nextStepNo, string input, string output, CancellationToken cancellationToken)
    {
        var completedAt = DateTime.UtcNow;
        agentRun = agentRun with
        {
            Status = "Completed",
            CurrentStepNo = nextStepNo,
            OutputPayload = output,
            EndedAt = completedAt,
            LastModifyTime = completedAt
        };

        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
        await _agentStepRepository.AddAsync(CreateStep(
            agentRun.RunId, nextStepNo, "completed", "Completed",
            input, output, null, completedAt, completedAt), cancellationToken);

        return new CreateAgentRunResult(
            agentRun.RunId,
            agentRun.AgentCode,
            input,
            output,
            agentRun.Status);
    }

    private async Task<DecisionResult> GenerateDecisionAsync(
        AgentDefinitionVersion version, string input, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var modelResponse = await _modelGateway.GenerateTextAsync(
            new GenerateTextRequest(version.DefaultModel, BuildRuntimePrompt(version.SystemPromptTemplate), input),
            cancellationToken);
        var endedAt = DateTime.UtcNow;

        return new DecisionResult(
            _stepDecisionParser.Parse(modelResponse.Output),
            modelResponse.Output,
            startedAt,
            endedAt);
    }

    private static string BuildRuntimePrompt(string? systemPromptTemplate)
    {
        if (string.IsNullOrWhiteSpace(systemPromptTemplate))
            return RuntimeInstruction;

        return $"{systemPromptTemplate.Trim()}\n\n{RuntimeInstruction}";
    }

    private static string BuildToolFollowUpInput(string originalInput, ToolExecutionResult toolResult)
    {
        return
            $"""
            Original user input:
            {originalInput}

            Tool called:
            {toolResult.ToolName}

            Tool result:
            {toolResult.Output}

            Produce the final user-facing answer now.
            """;
    }

    private static string GetResponse(StepDecision decision)
    {
        if (string.IsNullOrWhiteSpace(decision.Response))
            throw new InvalidOperationException("Respond decision did not include a response.");

        return decision.Response.Trim();
    }

    private static void EnsureToolAllowed(string toolName, ResolvedAgentDefinition resolvedDefinition, string agentCode)
    {
        var allowedTools = AgentDefinitionToolListSerializer.Parse(resolvedDefinition.Version.AllowedTools);
        if (!allowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Tool '{toolName}' is not allowed for agent definition '{agentCode}'.");
    }

    private static AgentStep CreateStep(
        string runId, int stepNo, string stepType, string status,
        string? input, string? output, string? error,
        DateTime startedAt, DateTime endedAt)
    {
        return new AgentStep
        {
            StepId = Guid.NewGuid().ToString("N"),
            RunId = runId,
            StepNo = stepNo,
            StepType = stepType,
            Status = status,
            InputPayload = input,
            OutputPayload = output,
            ErrorPayload = error,
            StepKey = $"{runId}:{stepType}:{stepNo}",
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationMs = Math.Max(0, (long)(endedAt - startedAt).TotalMilliseconds),
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = startedAt,
            LastModifyTime = endedAt
        };
    }

    private sealed record DecisionResult(
        StepDecision Decision, string RawOutput, DateTime StartedAt, DateTime EndedAt);
}
