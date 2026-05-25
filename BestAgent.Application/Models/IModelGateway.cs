namespace BestAgent.Application.Models;

public interface IModelGateway
{
    Task<GenerateTextResult> GenerateTextAsync(GenerateTextRequest request, CancellationToken cancellationToken);
}
