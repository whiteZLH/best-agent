using BestAgent.Domain.Tools;
using MediatR;

namespace BestAgent.Application.Tools.Queries.GetToolDefinitions;

public class GetToolDefinitionsQueryHandler : IRequestHandler<GetToolDefinitionsQuery, IReadOnlyList<ToolDefinitionViewModel>>
{
    private readonly IToolDefinitionRepository _toolDefinitionRepository;

    public GetToolDefinitionsQueryHandler(
        IToolDefinitionRepository toolDefinitionRepository)
    {
        _toolDefinitionRepository = toolDefinitionRepository;
    }

    public async Task<IReadOnlyList<ToolDefinitionViewModel>> Handle(GetToolDefinitionsQuery request, CancellationToken cancellationToken)
    {
        var entities = request.EnabledOnly == true
            ? await _toolDefinitionRepository.GetEnabledAsync(cancellationToken)
            : await _toolDefinitionRepository.GetAllAsync(cancellationToken);

        return entities
            .Select(ToolDefinitionViewModel.FromEntity)
            .ToArray();
    }
}
