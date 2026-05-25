using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.AgentDefinitions;
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

    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BestAgentDbContext).Assembly);
    }
}
