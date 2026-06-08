namespace BestAgent.Domain.Knowledge;

public interface ISessionMemoryRepository
{
    Task AddAsync(SessionMemory memory, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionMemory>> ListActiveBySessionAsync(
        string tenantId,
        string sessionId,
        int maxCount,
        CancellationToken cancellationToken);
}
