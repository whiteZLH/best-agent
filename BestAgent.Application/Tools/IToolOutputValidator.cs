using BestAgent.Domain.Tools;

namespace BestAgent.Application.Tools;

public interface IToolOutputValidator
{
    void Validate(ToolDefinition definition, string output);
}
