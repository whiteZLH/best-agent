using BestAgent.Domain.AgentDefinitions;
using MediatR;

namespace BestAgent.Application.AgentDefinitions.Commands.ActivateAgentDefinitionVersion;

public class ActivateAgentDefinitionVersionCommandHandler
    : IRequestHandler<ActivateAgentDefinitionVersionCommand, AgentDefinitionViewModel>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;

    public ActivateAgentDefinitionVersionCommandHandler(IAgentDefinitionRepository agentDefinitionRepository)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
    }

    public async Task<AgentDefinitionViewModel> Handle(
        ActivateAgentDefinitionVersionCommand request,
        CancellationToken cancellationToken)
    {
        var resolvedDefinition = await _agentDefinitionRepository.GetByCodeAsync(request.AgentCode, cancellationToken);
        if (resolvedDefinition is null)
        {
            throw new InvalidOperationException($"Agent definition '{request.AgentCode}' was not found.");
        }

        var targetVersion = await _agentDefinitionRepository.GetVersionByCodeAsync(
            request.AgentCode,
            request.Version,
            cancellationToken);
        if (targetVersion is null)
        {
            throw new InvalidOperationException(
                $"Version '{request.Version}' was not found for agent definition '{request.AgentCode}'.");
        }

        var now = DateTime.UtcNow;
        var currentVersion = resolvedDefinition.Version.Version == targetVersion.Version
            ? null
            : resolvedDefinition.Version with
            {
                Status = AgentDefinitionVersionStatuses.Archived,
                LastModifier = "system",
                LastModifierName = "system",
                LastModifyTime = now
            };

        var activatedVersion = targetVersion with
        {
            Status = AgentDefinitionVersionStatuses.Published,
            PublishedAt = targetVersion.PublishedAt ?? now,
            LastModifier = "system",
            LastModifierName = "system",
            LastModifyTime = now
        };

        var updatedDefinition = resolvedDefinition.Definition with
        {
            CurrentVersion = targetVersion.Version,
            LastModifier = "system",
            LastModifierName = "system",
            LastModifyTime = now
        };

        await _agentDefinitionRepository.ActivateVersionAsync(
            updatedDefinition,
            activatedVersion,
            currentVersion,
            cancellationToken);

        return AgentDefinitionViewModel.FromResolvedDefinition(
            new ResolvedAgentDefinition(updatedDefinition, activatedVersion));
    }
}
