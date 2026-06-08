using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence.Repositories;

public class KnowledgeChunkRepository : IKnowledgeChunkRepository
{
    private readonly BestAgentDbContext _dbContext;

    public KnowledgeChunkRepository(BestAgentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddRangeAsync(IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken)
    {
        await _dbContext.KnowledgeChunks.AddRangeAsync(chunks, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> ListByDocumentIdAsync(string documentId, CancellationToken cancellationToken)
    {
        return await _dbContext.KnowledgeChunks
            .AsNoTracking()
            .Where(x => x.DocumentId == documentId && !x.Deleted)
            .OrderBy(x => x.ChunkNo)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> ListByKnowledgeSourceCodesAsync(
        string tenantId,
        IReadOnlyList<string> sourceCodes,
        string? queryText,
        int candidateCount,
        int maxCount,
        CancellationToken cancellationToken)
    {
        if (sourceCodes.Count == 0 || maxCount <= 0)
        {
            return Array.Empty<KnowledgeChunk>();
        }

        var normalizedCandidateCount = Math.Max(maxCount, candidateCount > 0 ? candidateCount : maxCount * 3);
        var query =
            from chunk in _dbContext.KnowledgeChunks.AsNoTracking()
            join document in _dbContext.KnowledgeDocuments.AsNoTracking()
                on chunk.DocumentId equals document.Id
            where sourceCodes.Contains(document.KnowledgeSourceCode)
                  && !chunk.Deleted
                  && !document.Deleted
            select new { chunk, document };

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(x => x.document.TenantId == tenantId && x.chunk.TenantId == tenantId);
        }

        var lexicalRanker = new KnowledgeChunkLexicalRanker(queryText);
        var rankedCandidates = await query
            .OrderBy(x => x.document.KnowledgeSourceCode)
            .ThenBy(x => x.document.DocumentCode)
            .ThenBy(x => x.chunk.ChunkNo)
            .Select(x => new RankedKnowledgeChunkCandidate(
                x.chunk,
                x.document.KnowledgeSourceCode,
                x.document.DocumentCode,
                x.document.Title,
                x.document.SourceUri))
            .ToArrayAsync(cancellationToken);

        return rankedCandidates
            .Select(candidate => new
            {
                candidate.Chunk,
                Score = lexicalRanker.Score(candidate)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.ChunkNo)
            .ThenBy(x => x.Chunk.Id, StringComparer.Ordinal)
            .Take(normalizedCandidateCount)
            .Take(maxCount)
            .Select(x => x.Chunk)
            .ToArray();
    }

    private sealed class KnowledgeChunkLexicalRanker
    {
        private readonly string[] _terms;

        public KnowledgeChunkLexicalRanker(string? queryText)
        {
            _terms = NormalizeTerms(queryText);
        }

        public int Score(RankedKnowledgeChunkCandidate candidate)
        {
            if (_terms.Length == 0)
            {
                return 0;
            }

            var haystack =
                $"{candidate.Chunk.Content} {candidate.Chunk.Source} {candidate.Chunk.Metadata} {candidate.KnowledgeSourceCode} {candidate.DocumentCode} {candidate.Title} {candidate.SourceUri}"
                    .ToLowerInvariant();
            var score = 0;
            foreach (var term in _terms)
            {
                if (term.Length == 0)
                {
                    continue;
                }

                var hitIndex = haystack.IndexOf(term, StringComparison.Ordinal);
                if (hitIndex < 0)
                {
                    continue;
                }

                score += 10;
                score += Math.Max(0, 5 - hitIndex / 80);
                score += CountOccurrences(haystack, term);
            }

            return score;
        }

        public static string[] NormalizeTerms(string? queryText)
        {
            if (string.IsNullOrWhiteSpace(queryText))
            {
                return [];
            }

            return queryText
                .ToLowerInvariant()
                .Split(
                    [' ', '\r', '\n', '\t', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\''],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(term => term.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .Take(12)
                .ToArray();
        }

        private static int CountOccurrences(string content, string term)
        {
            var count = 0;
            var index = 0;
            while (true)
            {
                index = content.IndexOf(term, index, StringComparison.Ordinal);
                if (index < 0)
                {
                    return count;
                }

                count++;
                index += term.Length;
            }
        }
    }

    private sealed record RankedKnowledgeChunkCandidate(
        KnowledgeChunk Chunk,
        string KnowledgeSourceCode,
        string DocumentCode,
        string Title,
        string SourceUri);
}
