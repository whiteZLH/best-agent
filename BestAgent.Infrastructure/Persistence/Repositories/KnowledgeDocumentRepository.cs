using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class KnowledgeDocumentRepository : IKnowledgeDocumentRepository
{
    private readonly BestAgentDbContext _dbContext;

    public KnowledgeDocumentRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        await _dbContext.KnowledgeDocuments.AddAsync(document, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<KnowledgeDocument?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        return _dbContext.KnowledgeDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListByKnowledgeSourceCodesAsync(
        string tenantId,
        IReadOnlyList<string> sourceCodes,
        CancellationToken cancellationToken)
    {
        if (sourceCodes.Count == 0)
        {
            return Array.Empty<KnowledgeDocument>();
        }

        var query = _dbContext.KnowledgeDocuments
            .AsNoTracking()
            .Where(x => sourceCodes.Contains(x.KnowledgeSourceCode) && !x.Deleted);

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        return await query
            .OrderBy(x => x.KnowledgeSourceCode)
            .ThenBy(x => x.DocumentCode)
            .ToArrayAsync(cancellationToken);
    }
}
