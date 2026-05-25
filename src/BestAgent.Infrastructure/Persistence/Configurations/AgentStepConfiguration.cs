using BestAgent.Domain.Steps;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestAgent.Infrastructure.Persistence.Configurations;

internal sealed class AgentStepConfiguration : IEntityTypeConfiguration<AgentStep>
{
    public void Configure(EntityTypeBuilder<AgentStep> builder)
    {
        builder.ToTable("agent_step");
        builder.HasKey(entity => entity.StepId);
        builder.Property(entity => entity.StepId).HasColumnName("step_id").HasMaxLength(64);
        builder.Property(entity => entity.RunId).HasColumnName("run_id").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.StepNo).HasColumnName("step_no").IsRequired();
        builder.Property(entity => entity.StepType).HasColumnName("step_type").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.InputPayload).HasColumnName("input_payload").IsRequired();
        builder.Property(entity => entity.OutputPayload).HasColumnName("output_payload");
        builder.Property(entity => entity.ErrorPayload).HasColumnName("error_payload");
        builder.Property(entity => entity.StepKey).HasColumnName("step_key").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.RetryCount).HasColumnName("retry_count").IsRequired();
        builder.Property(entity => entity.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.EndedAt).HasColumnName("ended_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.RunId, entity.StepKey }).IsUnique();
        builder.HasIndex(entity => entity.RunId);
        builder.ConfigureAuditColumns();
    }
}
