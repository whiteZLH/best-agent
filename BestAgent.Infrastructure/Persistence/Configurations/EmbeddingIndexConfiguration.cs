using System.Text.Json;
using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class EmbeddingIndexConfiguration : IEntityTypeConfiguration<EmbeddingIndex>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<EmbeddingIndex> builder)
    {
        builder.ToTable("embedding_index");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64);
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32);
        builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(64);
        builder.Property(x => x.ModelName).HasColumnName("model_name").HasMaxLength(128);
        builder.Property(x => x.VectorRef).HasColumnName("vector_ref").HasMaxLength(256);
        builder.Property(x => x.VectorPayload).HasColumnName("vector_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasConversion(JsonStringConverter);

        builder.HasIndex(x => new { x.SourceType, x.SourceId });
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.ModelName);
        builder.HasIndex(x => x.Deleted);

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<EmbeddingIndex> builder)
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
