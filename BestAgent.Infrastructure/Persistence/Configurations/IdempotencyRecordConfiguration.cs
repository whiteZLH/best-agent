using System.Text.Json;
using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_record");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(x => x.ScopeType).HasColumnName("scope_type").HasMaxLength(32);
        builder.Property(x => x.ScopeKey).HasColumnName("scope_key").HasMaxLength(128);
        builder.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(128);
        builder.Property(x => x.TargetId).HasColumnName("target_id").HasMaxLength(64);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.ExpireAt).HasColumnName("expire_at");
        builder.Property(x => x.ExtraPayload).HasColumnName("extra_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);

        builder.HasIndex(x => new { x.ScopeType, x.ScopeKey }).IsUnique();
        builder.HasIndex(x => x.TargetId);
        builder.HasIndex(x => x.ExpireAt);
        builder.HasIndex(x => x.Deleted);

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<IdempotencyRecord> builder)
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
