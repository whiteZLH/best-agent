using BestAgent.Domain.Knowledge;
using BestAgent.Infrastructure.Persistence;
using BestAgent.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BestAgent.Api.Tests.Infrastructure;

public class KnowledgeChunkRepositoryTests
{
    [Fact]
    public async Task ListByKnowledgeSourceCodesAsync_ShouldRerankChunksByQueryText()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 6, 7, 9, 0, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            dbContext.KnowledgeDocuments.AddRange(
                new KnowledgeDocument
                {
                    Id = "doc-1",
                    TenantId = "tenant-1",
                    KnowledgeSourceCode = "faq",
                    DocumentCode = "faq-1",
                    Title = "Travel FAQ",
                    SourceUri = "faq://travel",
                    ContentType = "text/plain",
                    ParseStatus = "parsed",
                    VersionNo = 1,
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                },
                new KnowledgeDocument
                {
                    Id = "doc-2",
                    TenantId = "tenant-1",
                    KnowledgeSourceCode = "faq",
                    DocumentCode = "faq-2",
                    Title = "Refund FAQ",
                    SourceUri = "faq://refund",
                    ContentType = "text/plain",
                    ParseStatus = "parsed",
                    VersionNo = 1,
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                });
            await dbContext.KnowledgeChunks.AddRangeAsync(
            [
                new KnowledgeChunk
                {
                    Id = "chunk-1",
                    DocumentId = "doc-1",
                    TenantId = "tenant-1",
                    ChunkNo = 1,
                    Content = "Flights can be changed within 24 hours.",
                    TokenCount = 8,
                    Source = "faq/doc-1#1",
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                },
                new KnowledgeChunk
                {
                    Id = "chunk-2",
                    DocumentId = "doc-2",
                    TenantId = "tenant-1",
                    ChunkNo = 1,
                    Content = "Refund requests require manager approval and refund form details.",
                    TokenCount = 10,
                    Source = "faq/doc-2#1",
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                }
            ]);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new KnowledgeChunkRepository(dbContext);

            var results = await repository.ListByKnowledgeSourceCodesAsync(
                "tenant-1",
                ["faq"],
                "refund approval details",
                10,
                2,
                CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.Equal("chunk-2", results[0].Id);
            Assert.Equal("chunk-1", results[1].Id);
        }
    }

    [Fact]
    public async Task ListByKnowledgeSourceCodesAsync_ShouldRecallRelevantChunkBeyondInitialStableOrdering()
    {
        var options = new DbContextOptionsBuilder<BestAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new DateTime(2026, 6, 7, 9, 30, 0, DateTimeKind.Utc);

        await using (var dbContext = new BestAgentDbContext(options))
        {
            dbContext.KnowledgeDocuments.AddRange(
                new KnowledgeDocument
                {
                    Id = "doc-1",
                    TenantId = "tenant-1",
                    KnowledgeSourceCode = "faq",
                    DocumentCode = "faq-1",
                    Title = "Travel FAQ",
                    SourceUri = "faq://travel",
                    ContentType = "text/plain",
                    ParseStatus = "parsed",
                    VersionNo = 1,
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                },
                new KnowledgeDocument
                {
                    Id = "doc-2",
                    TenantId = "tenant-1",
                    KnowledgeSourceCode = "faq",
                    DocumentCode = "faq-2",
                    Title = "Refund FAQ",
                    SourceUri = "faq://refund",
                    ContentType = "text/plain",
                    ParseStatus = "parsed",
                    VersionNo = 1,
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                });
            await dbContext.KnowledgeChunks.AddRangeAsync(
            [
                new KnowledgeChunk
                {
                    Id = "chunk-1",
                    DocumentId = "doc-1",
                    TenantId = "tenant-1",
                    ChunkNo = 1,
                    Content = "Flights can be changed within 24 hours.",
                    TokenCount = 8,
                    Source = "faq/doc-1#1",
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                },
                new KnowledgeChunk
                {
                    Id = "chunk-2",
                    DocumentId = "doc-1",
                    TenantId = "tenant-1",
                    ChunkNo = 2,
                    Content = "Boarding passes can be reissued at the gate.",
                    TokenCount = 8,
                    Source = "faq/doc-1#2",
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                },
                new KnowledgeChunk
                {
                    Id = "chunk-3",
                    DocumentId = "doc-2",
                    TenantId = "tenant-1",
                    ChunkNo = 1,
                    Content = "Refund requests require manager approval and refund form details.",
                    TokenCount = 10,
                    Source = "faq/doc-2#1",
                    Creator = "system",
                    CreatorName = "system",
                    LastModifier = "system",
                    LastModifierName = "system",
                    CreateTime = now,
                    LastModifyTime = now
                }
            ]);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new BestAgentDbContext(options))
        {
            var repository = new KnowledgeChunkRepository(dbContext);

            var results = await repository.ListByKnowledgeSourceCodesAsync(
                "tenant-1",
                ["faq"],
                "refund approval details",
                1,
                1,
                CancellationToken.None);

            var match = Assert.Single(results);
            Assert.Equal("chunk-3", match.Id);
        }
    }
}
