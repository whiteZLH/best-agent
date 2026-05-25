using BestAgent.Domain.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_record");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(entity => entity.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.RunId).HasColumnName("run_id").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.IdempotencyKey).IsUnique();
        builder.ConfigureAuditColumns();
    }
}
