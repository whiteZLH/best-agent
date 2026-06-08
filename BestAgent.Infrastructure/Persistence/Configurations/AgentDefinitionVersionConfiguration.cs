using BestAgent.Domain.AgentDefinitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class AgentDefinitionVersionConfiguration : IEntityTypeConfiguration<AgentDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<AgentDefinitionVersion> builder)
    {
        builder.ToTable("agent_definition_version");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(x => x.AgentDefinitionId).HasColumnName("agent_definition_id").HasMaxLength(64);
        builder.Property(x => x.Version).HasColumnName("version");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(256);
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.Instruction).HasColumnName("instruction");
        builder.Property(x => x.SystemPromptTemplate).HasColumnName("system_prompt_template");
        builder.Property(x => x.DefaultModel).HasColumnName("default_model").HasMaxLength(128);
        builder.Property(x => x.AllowedTools).HasColumnName("allowed_tools").HasColumnType("jsonb");
        builder.Property(x => x.DeniedTools).HasColumnName("denied_tools").HasColumnType("jsonb");
        builder.Property(x => x.KnowledgeSources).HasColumnName("knowledge_sources").HasColumnType("jsonb");
        builder.Property(x => x.MemoryPolicy).HasColumnName("memory_policy").HasColumnType("jsonb");
        builder.Property(x => x.RoutingPolicy).HasColumnName("routing_policy").HasColumnType("jsonb");
        builder.Property(x => x.ApprovalPolicy).HasColumnName("approval_policy").HasColumnType("jsonb");
        builder.Property(x => x.ExecutionPolicy).HasColumnName("execution_policy").HasColumnType("jsonb");
        builder.Property(x => x.PlannerPolicy).HasColumnName("planner_policy").HasColumnType("jsonb");
        builder.Property(x => x.ContextPolicy).HasColumnName("context_policy").HasColumnType("jsonb");
        builder.Property(x => x.AllowedHandoffs).HasColumnName("allowed_handoffs").HasColumnType("jsonb");
        builder.Property(x => x.OutputSchema).HasColumnName("output_schema").HasColumnType("jsonb");
        builder.Property(x => x.MaxTurns).HasColumnName("max_turns");
        builder.Property(x => x.MaxCost).HasColumnName("max_cost").HasPrecision(18, 6);
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<AgentDefinitionVersion> builder)
    {
        builder.Property(x => x.LastModifier).HasColumnName("last_modifier").HasMaxLength(64);
        builder.Property(x => x.LastModifyTime).HasColumnName("last_modify_time");
        builder.Property(x => x.LastModifierName).HasColumnName("last_modifier_name").HasMaxLength(128);
        builder.Property(x => x.CreateTime).HasColumnName("create_time");
        builder.Property(x => x.CreatorName).HasColumnName("creator_name").HasMaxLength(128);
        builder.Property(x => x.Creator).HasColumnName("creator").HasMaxLength(64);
        builder.Property(x => x.Deleted).HasColumnName("deleted");
    }
}
