using System.Text.Json;
using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class AgentStepConfiguration : IEntityTypeConfiguration<AgentStep>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<AgentStep> builder)
    {
        builder.ToTable("agent_step");
        builder.HasKey(x => x.StepId);

        builder.Property(x => x.StepId).HasColumnName("step_id").HasMaxLength(64);
        builder.Property(x => x.RunId).HasColumnName("run_id").HasMaxLength(64);
        builder.Property(x => x.StepNo).HasColumnName("step_no");
        builder.Property(x => x.StepType).HasColumnName("step_type").HasMaxLength(32);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.InputPayload).HasColumnName("input_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.OutputPayload).HasColumnName("output_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.ErrorPayload).HasColumnName("error_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.StepKey).HasColumnName("step_key").HasMaxLength(128);
        builder.Property(x => x.RetryCount).HasColumnName("retry_count");
        builder.Property(x => x.DependsOnStepId).HasColumnName("depends_on_step_id").HasMaxLength(64);
        builder.Property(x => x.DecisionPayload).HasColumnName("decision_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.StatusVersion).HasColumnName("status_version");
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.EndedAt).HasColumnName("ended_at");
        builder.Property(x => x.DurationMs).HasColumnName("duration_ms");

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<AgentStep> builder)
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
