namespace BestAgent.Domain.Knowledge;

public interface IEmbeddingIndexRepository
{
    Task AddAsync(EmbeddingIndex embeddingIndex, CancellationToken cancellationToken);

    Task<EmbeddingIndex?> GetBySourceAsync(
        string tenantId,
        string sourceType,
        string sourceId,
        CancellationToken cancellationToken);
}
