using System.Text.Json;
using BestAgent.Domain.AgentDefinitions;
using MediatR;

namespace BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinition;

public class CreateAgentDefinitionCommandHandler : IRequestHandler<CreateAgentDefinitionCommand, AgentDefinitionViewModel>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;

    public CreateAgentDefinitionCommandHandler(IAgentDefinitionRepository agentDefinitionRepository)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
    }

    public async Task<AgentDefinitionViewModel> Handle(CreateAgentDefinitionCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();
        var defaultModel = request.DefaultModel.Trim();
        var systemPromptTemplate = request.SystemPromptTemplate.Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Agent definition code is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Agent definition name is required.");
        }

        if (string.IsNullOrWhiteSpace(defaultModel))
        {
            throw new InvalidOperationException("Default model is required.");
        }

        if (string.IsNullOrWhiteSpace(systemPromptTemplate))
        {
            throw new InvalidOperationException("System prompt template is required.");
        }

        if (request.MaxTurns <= 0)
        {
            throw new InvalidOperationException("Max turns must be greater than zero.");
        }

        if (request.MaxCost < 0)
        {
            throw new InvalidOperationException("Max cost cannot be negative.");
        }

        if (await _agentDefinitionRepository.ExistsByCodeAsync(code, cancellationToken))
        {
            throw new InvalidOperationException($"Agent definition code '{code}' already exists.");
        }

        var now = DateTime.UtcNow;
        var definitionId = Guid.NewGuid().ToString("N");
        var versionId = Guid.NewGuid().ToString("N");
        var versionStatus = request.Enabled ? "Published" : "Draft";
        var allowedTools = request.AllowedTools?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var resolvedDefinition = new ResolvedAgentDefinition(
            new AgentDefinition
            {
                Id = definitionId,
                Code = code,
                Name = name,
                Description = request.Description?.Trim(),
                Enabled = request.Enabled,
                CurrentVersion = 1,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            },
            new AgentDefinitionVersion
            {
                Id = versionId,
                AgentDefinitionId = definitionId,
                Version = 1,
                Status = versionStatus,
                Name = $"{name} v1",
                Description = request.Description?.Trim(),
                Instruction = request.Instruction?.Trim(),
                SystemPromptTemplate = systemPromptTemplate,
                DefaultModel = defaultModel,
                AllowedTools = allowedTools is { Length: > 0 } ? JsonSerializer.Serialize(allowedTools) : null,
                MaxTurns = request.MaxTurns,
                MaxCost = request.MaxCost,
                PublishedAt = request.Enabled ? now : null,
                Creator = "system",
                CreatorName = "system",
                LastModifier = "system",
                LastModifierName = "system",
                CreateTime = now,
                LastModifyTime = now
            });

        await _agentDefinitionRepository.AddAsync(resolvedDefinition, cancellationToken);
        return AgentDefinitionViewModel.FromResolvedDefinition(resolvedDefinition);
    }
}
