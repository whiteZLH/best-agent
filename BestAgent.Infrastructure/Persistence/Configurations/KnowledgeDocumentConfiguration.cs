using System.Text.Json;
using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_document");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64);
        builder.Property(x => x.KnowledgeSourceCode).HasColumnName("knowledge_source_code").HasMaxLength(128);
        builder.Property(x => x.DocumentCode).HasColumnName("document_code").HasMaxLength(128);
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(512);
        builder.Property(x => x.SourceUri).HasColumnName("source_uri").HasMaxLength(1024);
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(64);
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.ParseStatus).HasColumnName("parse_status").HasMaxLength(32);
        builder.Property(x => x.VersionNo).HasColumnName("version_no");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.KnowledgeSourceCode);
        builder.HasIndex(x => x.DocumentCode);
        builder.HasIndex(x => x.Deleted);

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<KnowledgeDocument> builder)
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
