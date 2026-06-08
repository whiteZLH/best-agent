using System.Text.Json;
using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunk");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(x => x.DocumentId).HasColumnName("document_id").HasMaxLength(64);
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64);
        builder.Property(x => x.ChunkNo).HasColumnName("chunk_no");
        builder.Property(x => x.Content).HasColumnName("content");
        builder.Property(x => x.TokenCount).HasColumnName("token_count");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(512);
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasConversion(JsonStringConverter);

        builder.HasIndex(x => new { x.DocumentId, x.ChunkNo }).IsUnique();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.Deleted);

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<KnowledgeChunk> builder)
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
