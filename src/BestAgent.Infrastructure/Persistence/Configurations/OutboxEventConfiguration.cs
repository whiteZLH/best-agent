using BestAgent.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("run_outbox_event");
        builder.HasKey(entity => entity.EventId);
        builder.Property(entity => entity.EventId).HasColumnName("event_id").HasMaxLength(64);
        builder.Property(entity => entity.RunId).HasColumnName("run_id").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.SequenceNo).HasColumnName("seq_no").IsRequired();
        builder.Property(entity => entity.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(entity => entity.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.RunId, entity.SequenceNo }).IsUnique();
        builder.ConfigureAuditColumns();
    }
}
