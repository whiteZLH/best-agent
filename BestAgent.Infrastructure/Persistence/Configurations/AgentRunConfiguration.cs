using System.Text.Json;
using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("agent_run");
        builder.HasKey(x => x.RunId);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();

        builder.Property(x => x.RunId).HasColumnName("run_id").HasMaxLength(64);
        builder.Property(x => x.AgentCode).HasColumnName("agent_code").HasMaxLength(128);
        builder.Property(x => x.AgentDefinitionVersionId).HasColumnName("agent_definition_version_id").HasMaxLength(64);
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").HasMaxLength(64);
        builder.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64);
        builder.Property(x => x.SessionId).HasColumnName("session_id").HasMaxLength(64);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.InputPayload).HasColumnName("input_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.OutputPayload).HasColumnName("output_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.CurrentStepNo).HasColumnName("current_step_no");
        builder.Property(x => x.ParentRunId).HasColumnName("parent_run_id").HasMaxLength(64);
        builder.Property(x => x.RootRunId).HasColumnName("root_run_id").HasMaxLength(64);
        builder.Property(x => x.DelegatedByRunId).HasColumnName("delegated_by_run_id").HasMaxLength(64);
        builder.Property(x => x.DelegatedByAgent).HasColumnName("delegated_by_agent").HasMaxLength(128);
        builder.Property(x => x.StatusVersion).HasColumnName("status_version").IsConcurrencyToken();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(x => x.CurrentWaitToken).HasColumnName("current_wait_token").HasMaxLength(128);
        builder.Property(x => x.InterruptReason).HasColumnName("interrupt_reason").HasMaxLength(256);
        builder.Property(x => x.MaxTurns).HasColumnName("max_turns");
        builder.Property(x => x.MaxCost).HasColumnName("max_cost").HasPrecision(18, 6);
        builder.Property(x => x.TotalCost).HasColumnName("total_cost").HasPrecision(18, 6);
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.EndedAt).HasColumnName("ended_at");
        builder.Property(x => x.LastHeartbeatAt).HasColumnName("last_heartbeat_at");

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<AgentRun> builder)
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
