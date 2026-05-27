namespace BestAgent.Domain.Tools;

public interface IToolDefinitionRepository
{
    Task<ToolDefinition?> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<ToolDefinition?> GetByToolNameAsync(string toolName, CancellationToken cancellationToken);

    Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ToolDefinition>> GetEnabledAsync(CancellationToken cancellationToken);

    Task<bool> ExistsByToolNameAsync(string toolName, CancellationToken cancellationToken);

    Task<bool> AnyAsync(CancellationToken cancellationToken);

    Task AddAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken);

    Task UpdateAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken);

    Task DeleteAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken);
}
