using System.Linq.Expressions;
using BestAgent.Application.Abstractions;
using BestAgent.Domain.Agents;
using BestAgent.Domain.Common;
using BestAgent.Domain.Events;
using BestAgent.Domain.Idempotency;
using BestAgent.Domain.Messages;
using BestAgent.Domain.Runs;
using BestAgent.Domain.Steps;
using BestAgent.Domain.Tools;
using Microsoft.EntityFrameworkCore;

namespace BestAgent.Infrastructure.Persistence;

public sealed class BestAgentDbContext : DbContext, IUnitOfWork
{
    public BestAgentDbContext(DbContextOptions<BestAgentDbContext> options)
        : base(options)
    {
    }

    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();

    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();

    public DbSet<AgentMessage> AgentMessages => Set<AgentMessage>();

    public DbSet<ToolInvocation> ToolInvocations => Set<ToolInvocation>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BestAgentDbContext).Assembly);
        ApplySoftDeleteFilters(modelBuilder);
    }

    private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(type => typeof(AuditedEntity).IsAssignableFrom(type.ClrType)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "entity");
            var deletedProperty = Expression.Property(parameter, nameof(AuditedEntity.Deleted));
            var compareExpression = Expression.Equal(deletedProperty, Expression.Constant(false));
            var lambda = Expression.Lambda(compareExpression, parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }
}
