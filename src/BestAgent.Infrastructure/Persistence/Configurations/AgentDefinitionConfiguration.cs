using BestAgent.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

internal sealed class AgentDefinitionConfiguration : IEntityTypeConfiguration<AgentDefinition>
{
    public void Configure(EntityTypeBuilder<AgentDefinition> builder)
    {
        builder.ToTable("agent_definition");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(entity => entity.Code).HasColumnName("code").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.Description).HasColumnName("description");
        builder.Property(entity => entity.Instruction).HasColumnName("instruction");
        builder.Property(entity => entity.DefaultModel).HasColumnName("default_model").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.AllowedToolsJson).HasColumnName("allowed_tools").HasColumnType("jsonb").IsRequired();
        builder.Property(entity => entity.MaxTurns).HasColumnName("max_turns").IsRequired();
        builder.Property(entity => entity.Enabled).HasColumnName("enabled").IsRequired();
        builder.HasIndex(entity => entity.Code).IsUnique();
        builder.ConfigureAuditColumns();
    }
}
