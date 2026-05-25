using BestAgent.Domain.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

internal sealed class ToolInvocationConfiguration : IEntityTypeConfiguration<ToolInvocation>
{
    public void Configure(EntityTypeBuilder<ToolInvocation> builder)
    {
        builder.ToTable("tool_invocation");
        builder.HasKey(entity => entity.InvocationId);
        builder.Property(entity => entity.InvocationId).HasColumnName("invocation_id").HasMaxLength(64);
        builder.Property(entity => entity.RunId).HasColumnName("run_id").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.StepId).HasColumnName("step_id").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.ToolName).HasColumnName("tool_name").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.InputPayload).HasColumnName("input_payload").IsRequired();
        builder.Property(entity => entity.OutputPayload).HasColumnName("output_payload");
        builder.Property(entity => entity.ErrorPayload).HasColumnName("error_payload");
        builder.Property(entity => entity.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.EndedAt).HasColumnName("ended_at").HasColumnType("timestamp with time zone");
        builder.HasIndex(entity => entity.IdempotencyKey).IsUnique();
        builder.ConfigureAuditColumns();
    }
}
