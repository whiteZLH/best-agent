using AutoMapper;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CreateAgentRun;

public class CreateAgentRunCommandHandler : IRequestHandler<CreateAgentRunCommand, CreateAgentRunResult>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;
    private readonly IAgentRunRepository _agentRunRepository;
    private readonly IAgentStepRepository _agentStepRepository;

    public CreateAgentRunCommandHandler(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository,
        IAgentStepRepository agentStepRepository)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
        _agentRunRepository = agentRunRepository;
        _agentStepRepository = agentStepRepository;
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
            var output = BuildOutput(request.Input, resolvedDefinition);
            var completedAt = DateTime.UtcNow;
            agentRun = agentRun with
            {
                Status = "Completed",
                CurrentStepNo = 3,
                OutputPayload = output,
                EndedAt = completedAt,
                LastModifyTime = completedAt
            };

            await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
            await _agentStepRepository.AddAsync(CreateStep(
                runId,
                3,
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
            agentRun = agentRun with
            {
                Status = "Failed",
                InterruptReason = error,
                CurrentStepNo = 3,
                EndedAt = failedAt,
                LastModifyTime = failedAt
            };

            await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
            await _agentStepRepository.AddAsync(CreateStep(
                runId,
                3,
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

    private static string BuildOutput(string input, ResolvedAgentDefinition resolvedDefinition)
    {
        var agentName = string.IsNullOrWhiteSpace(resolvedDefinition.Definition.Name)
            ? resolvedDefinition.Definition.Code
            : resolvedDefinition.Definition.Name;

        return $"{agentName} processed the request: {input}";
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
