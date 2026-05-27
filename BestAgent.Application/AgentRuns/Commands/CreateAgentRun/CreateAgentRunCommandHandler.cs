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
            StatusVersion = 1,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        await _agentRunRepository.AddAsync(agentRun, cancellationToken);
        await _agentStepRepository.AddAsync(AgentRunLoop.CreateStep(runId, 1, "created", "Completed", request.Input, null, null, now, now), cancellationToken);
        await _agentStepRepository.AddAsync(AgentRunLoop.CreateStep(runId, 2, "running", "Completed", request.Input, null, null, now, now), cancellationToken);

        var loopContext = new AgentLoopContext(agentRun, resolvedDefinition.Version, request.Input, 3, 0);

        try
        {
            var loopResult = await AgentRunLoop.ExecuteAsync(
                loopContext, resolvedDefinition,
                _modelGateway, _stepDecisionParser, _toolExecutor, _agentStepRepository,
                cancellationToken);

            switch (loopResult)
            {
                case AgentLoopCompleted completed:
                    return await CompleteRun(agentRun, completed.Output, request.Input, cancellationToken);

                case AgentLoopSuspended suspended:
                    return await SuspendRun(agentRun, suspended.WaitToken, suspended.SuspendedAtStepNo, request.Input, cancellationToken);

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
                runId, loopContext.StartStepNo, "failed", "Failed",
                request.Input, null, error, failedAt, failedAt), cancellationToken);
            throw;
        }
    }

    private async Task<CreateAgentRunResult> CompleteRun(
        AgentRun agentRun, string output, string input, CancellationToken cancellationToken)
    {
        var completedAt = DateTime.UtcNow;
        agentRun = agentRun with
        {
            Status = "Completed",
            OutputPayload = output,
            EndedAt = completedAt,
            LastModifyTime = completedAt
        };

        await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
        await _agentStepRepository.AddAsync(AgentRunLoop.CreateStep(
            agentRun.RunId, 0, "completed", "Completed",
            input, output, null, completedAt, completedAt), cancellationToken);

        return new CreateAgentRunResult(agentRun.RunId, agentRun.AgentCode, input, output, agentRun.Status);
    }

    private async Task<CreateAgentRunResult> SuspendRun(
        AgentRun agentRun, string waitToken, int stepNo, string input, CancellationToken cancellationToken)
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

        return new CreateAgentRunResult(agentRun.RunId, agentRun.AgentCode, input, null, agentRun.Status, waitToken);
    }
}