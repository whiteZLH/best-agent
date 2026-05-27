using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Exceptions;
using BestAgent.Application.Models;
using BestAgent.Application.Tools;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;

public class ResumeAgentRunCommandHandler : IRequestHandler<ResumeAgentRunCommand, ResumeAgentRunResult>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IModelGateway _modelGateway;
    private readonly IStepDecisionParser _stepDecisionParser;
    private readonly IToolExecutor _toolExecutor;

    public ResumeAgentRunCommandHandler(
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

    public async Task<ResumeAgentRunResult> Handle(ResumeAgentRunCommand request, CancellationToken cancellationToken)
    {
        var agentRun = await _agentRunRepository.GetByRunIdAsync(request.RunId, cancellationToken);
        if (agentRun is null)
            throw new NotFoundException("AgentRun", request.RunId);

        if (agentRun.Status != "WaitingTool")
            throw new ConflictException($"Run '{request.RunId}' is in status '{agentRun.Status}', expected 'WaitingTool'.");

        if (agentRun.CurrentWaitToken != request.WaitToken)
            throw new ConflictException($"Wait token mismatch for run '{request.RunId}'.");

        var resolvedDefinition = await _agentDefinitionRepository.GetEnabledByCodeAsync(agentRun.AgentCode, cancellationToken);
        if (resolvedDefinition is null)
            throw new NotFoundException("AgentDefinition", agentRun.AgentCode);

        var pendingStep = await _agentStepRepository.GetLastByRunIdAsync(request.RunId, cancellationToken);
        if (pendingStep is not null && pendingStep.Status == "Pending")
        {
            var completedAt = DateTime.UtcNow;
            pendingStep = pendingStep with
            {
                Status = "Completed",
                OutputPayload = request.ToolResult,
                EndedAt = completedAt,
                LastModifyTime = completedAt
            };
            await _agentStepRepository.UpdateAsync(pendingStep, cancellationToken);
        }

        var followUpInput = BuildToolFollowUpInput(agentRun.InputPayload ?? string.Empty, request.ToolResult);
        var nextStepNo = agentRun.CurrentStepNo + 1;

        agentRun = agentRun with
        {
            Status = "Running",
            CurrentWaitToken = string.Empty,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = DateTime.UtcNow
        };
        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);

        var loopContext = new AgentLoopContext(agentRun, resolvedDefinition.Version, followUpInput, nextStepNo, 0);

        try
        {
            var loopResult = await AgentRunLoop.ExecuteAsync(
                loopContext, resolvedDefinition,
                _modelGateway, _stepDecisionParser, _toolExecutor, _agentStepRepository,
                cancellationToken);

            switch (loopResult)
            {
                case AgentLoopCompleted completed:
                    return await CompleteRun(agentRun, completed.Output, cancellationToken);

                case AgentLoopSuspended suspended:
                    return await SuspendRun(agentRun, suspended.WaitToken, suspended.SuspendedAtStepNo, cancellationToken);

                default:
                    throw new InvalidOperationException("Unexpected loop result.");
            }
        }
        catch (Exception ex)
        {
            var failedAt = DateTime.UtcNow;
            var error = ex.Message[..Math.Min(ex.Message.Length, 256)];
            agentRun = agentRun with
            {
                Status = "Failed",
                InterruptReason = error,
                EndedAt = failedAt,
                LastModifyTime = failedAt
            };

            await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
            await _agentStepRepository.AddAsync(AgentRunLoop.CreateStep(
                agentRun.RunId, nextStepNo, "failed", "Failed",
                agentRun.InputPayload, null, error, failedAt, failedAt), cancellationToken);
            throw;
        }
    }

    private async Task<ResumeAgentRunResult> CompleteRun(
        AgentRun agentRun, string output, CancellationToken cancellationToken)
    {
        var completedAt = DateTime.UtcNow;
        agentRun = agentRun with
        {
            Status = "Completed",
            OutputPayload = output,
            EndedAt = completedAt,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = completedAt
        };

        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
        await _agentStepRepository.AddAsync(AgentRunLoop.CreateStep(
            agentRun.RunId, 0, "completed", "Completed",
            agentRun.InputPayload, output, null, completedAt, completedAt), cancellationToken);

        return new ResumeAgentRunResult(agentRun.RunId, agentRun.AgentCode, agentRun.InputPayload, output, agentRun.Status);
    }

    private async Task<ResumeAgentRunResult> SuspendRun(
        AgentRun agentRun, string waitToken, int stepNo, CancellationToken cancellationToken)
    {
        var suspendedAt = DateTime.UtcNow;
        agentRun = agentRun with
        {
            Status = "WaitingTool",
            CurrentWaitToken = waitToken,
            CurrentStepNo = stepNo,
            StatusVersion = agentRun.StatusVersion + 1,
            LastModifyTime = suspendedAt
        };

        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);

        return new ResumeAgentRunResult(agentRun.RunId, agentRun.AgentCode, agentRun.InputPayload, null, agentRun.Status, waitToken);
    }

    private static string BuildToolFollowUpInput(string originalInput, string toolResult)
    {
        return
            $"""
            Original user input:
            {originalInput}

            Tool result:
            {toolResult}

            Produce the final user-facing answer now.
            """;
    }
}