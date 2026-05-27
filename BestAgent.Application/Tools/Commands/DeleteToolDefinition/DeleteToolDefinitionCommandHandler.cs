using BestAgent.Application.Exceptions;
using BestAgent.Domain.Tools;
using MediatR;

namespace BestAgent.Application.Tools.Commands.DeleteToolDefinition;

public class DeleteToolDefinitionCommandHandler : IRequestHandler<DeleteToolDefinitionCommand>
{
    private readonly IToolDefinitionRepository _toolDefinitionRepository;

    public DeleteToolDefinitionCommandHandler(IToolDefinitionRepository toolDefinitionRepository)
    {
        _toolDefinitionRepository = toolDefinitionRepository;
    }

    public async Task Handle(DeleteToolDefinitionCommand request, CancellationToken cancellationToken)
    {
        var existing = await _toolDefinitionRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existing is null)
        {
            throw new NotFoundException("ToolDefinition", request.Id);
        }

        await _toolDefinitionRepository.DeleteAsync(existing, cancellationToken);
    }
}
