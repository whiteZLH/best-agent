namespace BestAgent.Domain.Knowledge;

public interface IKnowledgeDocumentRepository
{
    Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken);

    Task<KnowledgeDocument?> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeDocument>> ListByKnowledgeSourceCodesAsync(
        string tenantId,
        IReadOnlyList<string> sourceCodes,
        CancellationToken cancellationToken);
}
