using BestAgent.Domain.Tools;

namespace BestAgent.Application.Abstractions;

public interface IToolRegistry
{
    ToolDefinition? Get(string toolName);
}
