using System.Text.Json;
using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class RunOutboxEventConfiguration : IEntityTypeConfiguration<RunOutboxEvent>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<RunOutboxEvent> builder)
    {
        builder.ToTable("run_outbox_event");
        builder.HasKey(x => x.EventId);

        builder.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(64);
        builder.Property(x => x.RunId).HasColumnName("run_id").HasMaxLength(64);
        builder.Property(x => x.SeqNo).HasColumnName("seq_no");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(64);
        builder.Property(x => x.RunStatus).HasColumnName("run_status").HasMaxLength(32);
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.PublishStatus).HasColumnName("publish_status").HasMaxLength(32);
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.Property(x => x.RetryCount).HasColumnName("retry_count");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");

        builder.HasIndex(x => new { x.RunId, x.SeqNo }).IsUnique();
        builder.HasIndex(x => x.PublishStatus);
        builder.HasIndex(x => x.EventType);
        builder.HasIndex(x => x.Deleted);

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<RunOutboxEvent> builder)
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
