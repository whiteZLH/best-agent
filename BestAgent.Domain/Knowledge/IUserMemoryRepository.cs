namespace BestAgent.Domain.Knowledge;

public interface IUserMemoryRepository
{
    Task AddAsync(UserMemory memory, CancellationToken cancellationToken);

    Task UpdateAsync(UserMemory memory, CancellationToken cancellationToken);

    Task<UserMemory?> GetByMemoryKeyAsync(
        string tenantId,
        string userId,
        string memoryKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserMemory>> ListActiveByUserAsync(
        string tenantId,
        string userId,
        int maxCount,
        CancellationToken cancellationToken);
}
