using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.Knowledge;
using BestAgent.Domain.Tools;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence;

public class BestAgentDbContext : DbContext
{
    public BestAgentDbContext(DbContextOptions<BestAgentDbContext> options)
        : base(options)
    {
    }

    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();

    public DbSet<AgentDefinitionVersion> AgentDefinitionVersions => Set<AgentDefinitionVersion>();

    public DbSet<RouteRule> RouteRules => Set<RouteRule>();

    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();

    public DbSet<AgentApproval> AgentApprovals => Set<AgentApproval>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<RunOutboxEvent> RunOutboxEvents => Set<RunOutboxEvent>();

    public DbSet<ToolDefinition> ToolDefinitions => Set<ToolDefinition>();

    public DbSet<ToolInvocation> ToolInvocations => Set<ToolInvocation>();

    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();

    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();

    public DbSet<EmbeddingIndex> EmbeddingIndexes => Set<EmbeddingIndex>();

    public DbSet<SessionMemory> SessionMemories => Set<SessionMemory>();

    public DbSet<UserMemory> UserMemories => Set<UserMemory>();

    public DbSet<SummaryMemory> SummaryMemories => Set<SummaryMemory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BestAgentDbContext).Assembly);
    }
}
