using AutoMapper;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using BestAgent.Application.Models;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CreateAgentRun;

public class CreateAgentRunCommandHandler : IRequestHandler<CreateAgentRunCommand, CreateAgentRunResult>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;
    private readonly IModelGateway _modelGateway;

    public CreateAgentRunCommandHandler(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository,
        IModelGateway modelGateway)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
        _modelGateway = modelGateway;
    }

    public async Task<CreateAgentRunResult> Handle(CreateAgentRunCommand request, CancellationToken cancellationToken)
    {
        var resolvedDefinition = await _agentDefinitionRepository.GetEnabledByCodeAsync(request.AgentCode, cancellationToken);
        if (resolvedDefinition is null)
        {
            throw new InvalidOperationException($"Enabled agent definition was not found for code '{request.AgentCode}'.");
        }

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
        await _agentStepRepository.AddAsync(CreateStep(
            runId,
            1,
            "created",
            "Completed",
            request.Input,
            null,
            null,
            now,
            now), cancellationToken);
        await _agentStepRepository.AddAsync(CreateStep(
            runId,
            2,
            "running",
            "Completed",
            request.Input,
            null,
            null,
            now,
            now), cancellationToken);

        try
        {
            var modelCallStartedAt = DateTime.UtcNow;
            var modelResponse = await _modelGateway.GenerateTextAsync(
                new GenerateTextRequest(
                    resolvedDefinition.Version.DefaultModel,
                    resolvedDefinition.Version.SystemPromptTemplate,
                    request.Input),
                cancellationToken);
            var modelCallCompletedAt = DateTime.UtcNow;
            await _agentStepRepository.AddAsync(CreateStep(
                runId,
                3,
                "model_call",
                "Completed",
                request.Input,
                modelResponse.Output,
                null,
                modelCallStartedAt,
                modelCallCompletedAt), cancellationToken);

            var output = modelResponse.Output;
            var completedAt = DateTime.UtcNow;
            agentRun = agentRun with
            {
                Status = "Completed",
                CurrentStepNo = 4,
                OutputPayload = output,
                EndedAt = completedAt,
                LastModifyTime = completedAt
            };

            await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
            await _agentStepRepository.AddAsync(CreateStep(
                runId,
                4,
                "completed",
                "Completed",
                request.Input,
                output,
                null,
                completedAt,
                completedAt), cancellationToken);

            return new CreateAgentRunResult(
                agentRun.RunId,
                agentRun.AgentCode,
                request.Input,
                agentRun.OutputPayload,
                agentRun.Status);
        }
        catch (Exception ex)
        {
            var failedAt = DateTime.UtcNow;
            var error = ex.Message[..Math.Min(ex.Message.Length, 256)];
            await _agentStepRepository.AddAsync(CreateStep(
                runId,
                3,
                "model_call",
                "Failed",
                request.Input,
                null,
                error,
                failedAt,
                failedAt), cancellationToken);
            agentRun = agentRun with
            {
                Status = "Failed",
                InterruptReason = error,
                CurrentStepNo = 4,
                EndedAt = failedAt,
                LastModifyTime = failedAt
            };

            await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
            await _agentStepRepository.AddAsync(CreateStep(
                runId,
                4,
                "failed",
                "Failed",
                request.Input,
                null,
                error,
                failedAt,
                failedAt), cancellationToken);
            throw;
        }
    }

    private static AgentStep CreateStep(
        string runId,
        int stepNo,
        string stepType,
        string status,
        string? input,
        string? output,
        string? error,
        DateTime startedAt,
        DateTime endedAt)
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
            StepKey = $"{runId}:{stepType}",
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
