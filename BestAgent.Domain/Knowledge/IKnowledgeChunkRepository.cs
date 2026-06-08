namespace BestAgent.Domain.Knowledge;

public interface IKnowledgeChunkRepository
{
    Task AddRangeAsync(IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeChunk>> ListByDocumentIdAsync(string documentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeChunk>> ListByKnowledgeSourceCodesAsync(
        string tenantId,
        IReadOnlyList<string> sourceCodes,
        string? queryText,
        int candidateCount,
        int maxCount,
        CancellationToken cancellationToken);
}
