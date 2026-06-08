using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class EmbeddingIndexRepository : IEmbeddingIndexRepository
{
    private readonly BestAgentDbContext _dbContext;

    public EmbeddingIndexRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(EmbeddingIndex embeddingIndex, CancellationToken cancellationToken)
    {
        await _dbContext.EmbeddingIndexes.AddAsync(embeddingIndex, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<EmbeddingIndex?> GetBySourceAsync(
        string tenantId,
        string sourceType,
        string sourceId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.EmbeddingIndexes
            .AsNoTracking()
            .Where(x => x.SourceType == sourceType && x.SourceId == sourceId && !x.Deleted);

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }
}
