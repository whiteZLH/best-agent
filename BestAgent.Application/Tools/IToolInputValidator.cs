using BestAgent.Domain.Tools;

namespace BestAgent.Application.Tools;

public interface IToolInputValidator
{
    void Validate(ToolDefinition definition, string? input);
}
