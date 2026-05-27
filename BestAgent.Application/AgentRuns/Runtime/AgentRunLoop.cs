using BestAgent.Application.AgentDefinitions;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;

namespace BestAgent.Application.AgentRuns.Runtime;

internal static class AgentRunLoop
{
    private const string RuntimeInstruction =
        """
        Return JSON only.
        Use {"action":"respond","response":"..."} for a final answer.
        Use {"action":"tool_call","toolName":"...","toolInput":"..."} to call one tool.
        """;

    public static async Task<AgentLoopResult> ExecuteAsync(
        AgentLoopContext context,
        ResolvedAgentDefinition resolvedDefinition,
        IModelGateway modelGateway,
        IStepDecisionParser stepDecisionParser,
        IToolExecutor toolExecutor,
        IAgentStepRepository agentStepRepository,
        CancellationToken cancellationToken)
    {
        var run = context.Run;
        var version = context.Version;
        var currentInput = context.CurrentInput;
        var nextStepNo = context.StartStepNo;

        for (var turn = context.StartTurn; turn < run.MaxTurns; turn++)
        {
            var startedAt = DateTime.UtcNow;
            var modelResponse = await modelGateway.GenerateTextAsync(
                new GenerateTextRequest(version.DefaultModel, BuildRuntimePrompt(version.SystemPromptTemplate), currentInput),
                cancellationToken);
            var endedAt = DateTime.UtcNow;

            var decision = stepDecisionParser.Parse(modelResponse.Output);

            await agentStepRepository.AddAsync(CreateStep(
                run.RunId, nextStepNo++, "model_call", "Completed",
                currentInput, modelResponse.Output, null,
                startedAt, endedAt), cancellationToken);

            if (string.Equals(decision.Action, "respond", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(decision.Response))
                    throw new InvalidOperationException("Respond decision did not include a response.");
                return new AgentLoopCompleted(decision.Response.Trim());
            }

            if (!string.Equals(decision.Action, "tool_call", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported decision action '{decision.Action}'.");

            var toolName = decision.ToolName!;
            EnsureToolAllowed(toolName, resolvedDefinition, run.AgentCode);

            var toolStartedAt = DateTime.UtcNow;
            var toolResult = await toolExecutor.ExecuteAsync(
                toolName,
                decision.ToolInput,
                new ToolExecutionContext(run.RunId, run.AgentCode, context.CurrentInput),
                cancellationToken);
            var toolEndedAt = DateTime.UtcNow;

            if (toolResult.IsPending)
            {
                var waitToken = toolResult.WaitToken ?? Guid.NewGuid().ToString("N");
                await agentStepRepository.AddAsync(CreateStep(
                    run.RunId, nextStepNo, "tool_call", "Pending",
                    decision.ToolInput, null, null,
                    toolStartedAt, toolEndedAt), cancellationToken);
                return new AgentLoopSuspended(waitToken, nextStepNo);
            }

            await agentStepRepository.AddAsync(CreateStep(
                run.RunId, nextStepNo++, "tool_call", "Completed",
                decision.ToolInput, toolResult.Output, null,
                toolStartedAt, toolEndedAt), cancellationToken);

            currentInput = BuildToolFollowUpInput(context.CurrentInput, toolResult);
        }

        throw new InvalidOperationException("Max turns exceeded.");
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

    private static void EnsureToolAllowed(string toolName, ResolvedAgentDefinition resolvedDefinition, string agentCode)
    {
        var allowedTools = AgentDefinitionToolListSerializer.Parse(resolvedDefinition.Version.AllowedTools);
        if (!allowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Tool '{toolName}' is not allowed for agent definition '{agentCode}'.");
    }

    internal static AgentStep CreateStep(
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
}