namespace BestAgent.Domain.Knowledge;

public interface ISummaryMemoryRepository
{
    Task AddAsync(SummaryMemory memory, CancellationToken cancellationToken);

    Task<SummaryMemory?> GetLatestActiveAsync(
        string tenantId,
        string sessionId,
        string runId,
        CancellationToken cancellationToken);
}
