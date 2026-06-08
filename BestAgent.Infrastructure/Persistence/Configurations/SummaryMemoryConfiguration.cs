using System.Text.Json;
using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class SummaryMemoryConfiguration : IEntityTypeConfiguration<SummaryMemory>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<SummaryMemory> builder)
    {
        builder.ToTable("summary_memory");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64);
        builder.Property(x => x.RunId).HasColumnName("run_id").HasMaxLength(64);
        builder.Property(x => x.SessionId).HasColumnName("session_id").HasMaxLength(64);
        builder.Property(x => x.SummaryType).HasColumnName("summary_type").HasMaxLength(32);
        builder.Property(x => x.SourceStartSeq).HasColumnName("source_start_seq");
        builder.Property(x => x.SourceEndSeq).HasColumnName("source_end_seq");
        builder.Property(x => x.SummaryText).HasColumnName("summary_text");
        builder.Property(x => x.SummaryJson).HasColumnName("summary_json").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.GeneratedByModel).HasColumnName("generated_by_model").HasMaxLength(128);
        builder.Property(x => x.GeneratedAt).HasColumnName("generated_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");

        builder.HasIndex(x => x.RunId);
        builder.HasIndex(x => new { x.TenantId, x.SessionId });
        builder.HasIndex(x => new { x.SourceStartSeq, x.SourceEndSeq });
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => x.Deleted);

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<SummaryMemory> builder)
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
