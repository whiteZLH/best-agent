using MediatR;

namespace BestAgent.Application.Tools.Queries.GetToolDefinitionByName;

public record GetToolDefinitionByNameQuery(string ToolName) : IRequest<ToolDefinitionViewModel?>;
