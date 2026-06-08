using System.Text.Json;
using BestAgent.Domain.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class ToolInvocationConfiguration : IEntityTypeConfiguration<ToolInvocation>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<ToolInvocation> builder)
    {
        builder.ToTable("tool_invocation");
        builder.HasKey(x => x.InvocationId);

        builder.Property(x => x.InvocationId).HasColumnName("invocation_id").HasMaxLength(64);
        builder.Property(x => x.RunId).HasColumnName("run_id").HasMaxLength(64);
        builder.Property(x => x.StepId).HasColumnName("step_id").HasMaxLength(64);
        builder.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(128);
        builder.Property(x => x.Mode).HasColumnName("mode").HasMaxLength(16);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.InputPayload).HasColumnName("input_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.OutputPayload).HasColumnName("output_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.ErrorPayload).HasColumnName("error_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(x => x.CallbackToken).HasColumnName("callback_token").HasMaxLength(128);
        builder.Property(x => x.ExecutorNode).HasColumnName("executor_node").HasMaxLength(128);
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.EndedAt).HasColumnName("ended_at");
        builder.Property(x => x.DurationMs).HasColumnName("duration_ms");

        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.RunId);
        builder.HasIndex(x => x.StepId);
        builder.HasIndex(x => new { x.ToolName, x.Status });
        builder.HasIndex(x => x.Deleted);

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<ToolInvocation> builder)
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
