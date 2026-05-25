using AutoMapper;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;
using MediatR;

namespace BestAgent.Application.AgentRuns.Commands.CreateAgentRun;

public class CreateAgentRunCommandHandler : IRequestHandler<CreateAgentRunCommand, CreateAgentRunResult>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;
    private readonly IAgentRunRepository _agentRunRepository;

    public CreateAgentRunCommandHandler(
        IAgentDefinitionRepository agentDefinitionRepository,
        IAgentRunRepository agentRunRepository)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
        _agentRunRepository = agentRunRepository;
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

        try
        {
            var output = BuildOutput(request.Input, resolvedDefinition);
            var completedAt = DateTime.UtcNow;
            agentRun = agentRun with
            {
                Status = "Completed",
                OutputPayload = output,
                EndedAt = completedAt,
                LastModifyTime = completedAt
            };

            await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);

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
            agentRun = agentRun with
            {
                Status = "Failed",
                InterruptReason = ex.Message[..Math.Min(ex.Message.Length, 256)],
                EndedAt = failedAt,
                LastModifyTime = failedAt
            };

            await _agentRunRepository.UpdateAsync(agentRun, cancellationToken);
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
}
