using BestAgent.Domain.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

internal sealed class AgentMessageConfiguration : IEntityTypeConfiguration<AgentMessage>
{
    public void Configure(EntityTypeBuilder<AgentMessage> builder)
    {
        builder.ToTable("agent_message");
        builder.HasKey(entity => entity.MessageId);
        builder.Property(entity => entity.MessageId).HasColumnName("message_id").HasMaxLength(64);
        builder.Property(entity => entity.RunId).HasColumnName("run_id").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Role).HasColumnName("role").HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.Content).HasColumnName("content").IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.RunId);
        builder.ConfigureAuditColumns();
    }
}
