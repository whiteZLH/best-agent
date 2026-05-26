using BestAgent.Domain.AgentDefinitions;
using MediatR;

namespace BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinitionVersion;

public class CreateAgentDefinitionVersionCommandHandler
    : IRequestHandler<CreateAgentDefinitionVersionCommand, AgentDefinitionVersionViewModel>
{
    private readonly IAgentDefinitionRepository _agentDefinitionRepository;

    public CreateAgentDefinitionVersionCommandHandler(IAgentDefinitionRepository agentDefinitionRepository)
    {
        _agentDefinitionRepository = agentDefinitionRepository;
    }

    public async Task<AgentDefinitionVersionViewModel> Handle(
        CreateAgentDefinitionVersionCommand request,
        CancellationToken cancellationToken)
    {
        var definition = await _agentDefinitionRepository.GetByCodeAsync(request.AgentCode, cancellationToken);
        if (definition is null)
        {
            throw new InvalidOperationException($"Agent definition '{request.AgentCode}' was not found.");
        }

        var defaultModel = request.DefaultModel.Trim();
        var systemPromptTemplate = request.SystemPromptTemplate.Trim();
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

        var existingVersions = await _agentDefinitionRepository.GetVersionsAsync(request.AgentCode, cancellationToken);
        var nextVersionNumber = existingVersions.Count == 0 ? 1 : existingVersions.Max(x => x.Version) + 1;
        var now = DateTime.UtcNow;
        var versionName = string.IsNullOrWhiteSpace(request.Name)
            ? $"{definition.Definition.Name} v{nextVersionNumber}"
            : request.Name.Trim();

        var version = new AgentDefinitionVersion
        {
            Id = Guid.NewGuid().ToString("N"),
            AgentDefinitionId = definition.Definition.Id,
            Version = nextVersionNumber,
            Status = AgentDefinitionVersionStatuses.Draft,
            Name = versionName,
            Description = request.Description?.Trim(),
            Instruction = request.Instruction?.Trim(),
            SystemPromptTemplate = systemPromptTemplate,
            DefaultModel = defaultModel,
            AllowedTools = AgentDefinitionToolListSerializer.Serialize(request.AllowedTools),
            MaxTurns = request.MaxTurns,
            MaxCost = request.MaxCost,
            Creator = "system",
            CreatorName = "system",
            LastModifier = "system",
            LastModifierName = "system",
            CreateTime = now,
            LastModifyTime = now
        };

        await _agentDefinitionRepository.AddVersionAsync(version, cancellationToken);
        return AgentDefinitionVersionViewModel.FromVersion(version, definition.Definition.CurrentVersion);
    }
}
