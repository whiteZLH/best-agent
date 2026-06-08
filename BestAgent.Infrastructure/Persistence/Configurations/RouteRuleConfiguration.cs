using BestAgent.Domain.AgentDefinitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class RouteRuleConfiguration : IEntityTypeConfiguration<RouteRule>
{
    public void Configure(EntityTypeBuilder<RouteRule> builder)
    {
        builder.ToTable("route_rule");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(x => x.AgentDefinitionVersionId).HasColumnName("agent_definition_version_id").HasMaxLength(64);
        builder.Property(x => x.SourceAgentCode).HasColumnName("source_agent_code").HasMaxLength(128);
        builder.Property(x => x.TargetAgentCode).HasColumnName("target_agent_code").HasMaxLength(128);
        builder.Property(x => x.RuleName).HasColumnName("rule_name").HasMaxLength(128);
        builder.Property(x => x.Priority).HasColumnName("priority");
        builder.Property(x => x.MatchType).HasColumnName("match_type").HasMaxLength(64);
        builder.Property(x => x.MatchExpression).HasColumnName("match_expression").HasColumnType("jsonb");
        builder.Property(x => x.HandoffMode).HasColumnName("handoff_mode").HasMaxLength(32);
        builder.Property(x => x.ContextScope).HasColumnName("context_scope").HasColumnType("jsonb");
        builder.Property(x => x.MemoryScope).HasColumnName("memory_scope").HasColumnType("jsonb");
        builder.Property(x => x.ToolScope).HasColumnName("tool_scope").HasColumnType("jsonb");
        builder.Property(x => x.KnowledgeScope).HasColumnName("knowledge_scope").HasColumnType("jsonb");
        builder.Property(x => x.ApprovalRequired).HasColumnName("approval_required");
        builder.Property(x => x.Enabled).HasColumnName("enabled");

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<RouteRule> builder)
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
