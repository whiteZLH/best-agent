using MediatR;

namespace BestAgent.Application.Tools.Commands.DeleteToolDefinition;

public record DeleteToolDefinitionCommand(string Id) : IRequest;
