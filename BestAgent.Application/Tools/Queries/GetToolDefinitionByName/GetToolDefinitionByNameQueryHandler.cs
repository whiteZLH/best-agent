using BestAgent.Domain.Tools;
using MediatR;

namespace BestAgent.Application.Tools.Queries.GetToolDefinitionByName;

public class GetToolDefinitionByNameQueryHandler : IRequestHandler<GetToolDefinitionByNameQuery, ToolDefinitionViewModel?>
{
    private readonly IToolDefinitionRepository _toolDefinitionRepository;

    public GetToolDefinitionByNameQueryHandler(
        IToolDefinitionRepository toolDefinitionRepository)
    {
        _toolDefinitionRepository = toolDefinitionRepository;
    }

    public async Task<ToolDefinitionViewModel?> Handle(GetToolDefinitionByNameQuery request, CancellationToken cancellationToken)
    {
        var entity = await _toolDefinitionRepository.GetByToolNameAsync(request.ToolName, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        return ToolDefinitionViewModel.FromEntity(entity);
    }
}
