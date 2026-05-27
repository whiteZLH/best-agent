namespace BestAgent.Application.Tools;

public interface IToolHandlerRegistry
{
    bool HasHandler(string toolName);

    IReadOnlyCollection<string> GetRegisteredHandlerNames();
}
