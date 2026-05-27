using MediatR;

namespace BestAgent.Application.Tools.Queries.GetToolDefinitions;

public record GetToolDefinitionsQuery(bool? EnabledOnly = null) : IRequest<IReadOnlyList<ToolDefinitionViewModel>>;
