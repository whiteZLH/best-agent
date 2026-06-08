using System.Text.Json;
using BestAgent.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class UserMemoryConfiguration : IEntityTypeConfiguration<UserMemory>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<UserMemory> builder)
    {
        builder.ToTable("user_memory");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64);
        builder.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64);
        builder.Property(x => x.MemoryKey).HasColumnName("memory_key").HasMaxLength(128);
        builder.Property(x => x.MemoryScope).HasColumnName("memory_scope").HasMaxLength(64);
        builder.Property(x => x.MemoryType).HasColumnName("memory_type").HasMaxLength(32);
        builder.Property(x => x.MemoryValue).HasColumnName("memory_value").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32);
        builder.Property(x => x.SourceRef).HasColumnName("source_ref").HasMaxLength(128);
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        builder.Property(x => x.EffectiveAt).HasColumnName("effective_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");

        builder.HasIndex(x => new { x.TenantId, x.UserId, x.MemoryKey }).IsUnique();
        builder.HasIndex(x => x.MemoryScope);
        builder.HasIndex(x => x.MemoryType);
        builder.HasIndex(x => x.EffectiveAt);
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => x.Deleted);

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<UserMemory> builder)
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
