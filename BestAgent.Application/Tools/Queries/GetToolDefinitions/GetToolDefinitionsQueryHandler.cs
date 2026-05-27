using BestAgent.Domain.Tools;
using MediatR;

namespace BestAgent.Application.Tools.Queries.GetToolDefinitions;

public class GetToolDefinitionsQueryHandler : IRequestHandler<GetToolDefinitionsQuery, IReadOnlyList<ToolDefinitionViewModel>>
{
    private readonly IToolDefinitionRepository _toolDefinitionRepository;
    private readonly IToolHandlerRegistry _toolHandlerRegistry;

    public GetToolDefinitionsQueryHandler(
        IToolDefinitionRepository toolDefinitionRepository,
        IToolHandlerRegistry toolHandlerRegistry)
    {
        _toolDefinitionRepository = toolDefinitionRepository;
        _toolHandlerRegistry = toolHandlerRegistry;
    }

    public async Task<IReadOnlyList<ToolDefinitionViewModel>> Handle(GetToolDefinitionsQuery request, CancellationToken cancellationToken)
    {
        var entities = request.EnabledOnly == true
            ? await _toolDefinitionRepository.GetEnabledAsync(cancellationToken)
            : await _toolDefinitionRepository.GetAllAsync(cancellationToken);

        return entities
            .Select(e => ToolDefinitionViewModel.FromEntity(e, _toolHandlerRegistry.HasHandler(e.ToolName)))
            .ToArray();
    }
}
