using BestAgent.Domain.Tools;
using MediatR;

namespace BestAgent.Application.Tools.Queries.GetToolDefinitionByName;

public class GetToolDefinitionByNameQueryHandler : IRequestHandler<GetToolDefinitionByNameQuery, ToolDefinitionViewModel?>
{
    private readonly IToolDefinitionRepository _toolDefinitionRepository;
    private readonly IToolHandlerRegistry _toolHandlerRegistry;

    public GetToolDefinitionByNameQueryHandler(
        IToolDefinitionRepository toolDefinitionRepository,
        IToolHandlerRegistry toolHandlerRegistry)
    {
        _toolDefinitionRepository = toolDefinitionRepository;
        _toolHandlerRegistry = toolHandlerRegistry;
    }

    public async Task<ToolDefinitionViewModel?> Handle(GetToolDefinitionByNameQuery request, CancellationToken cancellationToken)
    {
        var entity = await _toolDefinitionRepository.GetByToolNameAsync(request.ToolName, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        return ToolDefinitionViewModel.FromEntity(entity, _toolHandlerRegistry.HasHandler(entity.ToolName));
    }
}
