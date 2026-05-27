using BestAgent.Domain.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class ToolDefinitionConfiguration : IEntityTypeConfiguration<ToolDefinition>
{
    public void Configure(EntityTypeBuilder<ToolDefinition> builder)
    {
        builder.ToTable("tool_definition");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(128);
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(256);
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.InputSchema).HasColumnName("input_schema").HasColumnType("jsonb");
        builder.Property(x => x.OutputSchema).HasColumnName("output_schema").HasColumnType("jsonb");
        builder.Property(x => x.SideEffectLevel).HasColumnName("side_effect_level").HasMaxLength(32);
        builder.Property(x => x.TimeoutMs).HasColumnName("timeout_ms");
        builder.Property(x => x.RetryPolicy).HasColumnName("retry_policy").HasColumnType("jsonb");
        builder.Property(x => x.AuthPolicy).HasColumnName("auth_policy").HasColumnType("jsonb");
        builder.Property(x => x.IdempotencyPolicy).HasColumnName("idempotency_policy").HasColumnType("jsonb");
        builder.Property(x => x.AsyncSupported).HasColumnName("async_supported");
        builder.Property(x => x.ConsistencyMode).HasColumnName("consistency_mode").HasMaxLength(32);
        builder.Property(x => x.CompensationPolicy).HasColumnName("compensation_policy").HasColumnType("jsonb");
        builder.Property(x => x.Enabled).HasColumnName("enabled");

        builder.HasIndex(x => x.ToolName).IsUnique();

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<ToolDefinition> builder)
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
