using BestAgent.Domain.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

internal sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("agent_run");
        builder.HasKey(entity => entity.RunId);
        builder.Property(entity => entity.RunId).HasColumnName("run_id").HasMaxLength(64);
        builder.Property(entity => entity.AgentCode).HasColumnName("agent_code").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.InputPayload).HasColumnName("input_payload").HasColumnType("jsonb").IsRequired();
        builder.Property(entity => entity.OutputPayload).HasColumnName("output_payload").HasColumnType("jsonb");
        builder.Property(entity => entity.CurrentStepNo).HasColumnName("current_step_no").IsRequired();
        builder.Property(entity => entity.StatusVersion).HasColumnName("status_version").IsRequired();
        builder.Property(entity => entity.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.EndedAt).HasColumnName("ended_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.ErrorMessage).HasColumnName("error_message");
        builder.HasIndex(entity => entity.IdempotencyKey).IsUnique();
        builder.ConfigureAuditColumns();
    }
}
