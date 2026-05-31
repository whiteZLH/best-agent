using System.Text.Json;
using BestAgent.Domain.AgentRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BestAgent.Infrastructure.Persistence.Configurations;

public class AgentApprovalConfiguration : IEntityTypeConfiguration<AgentApproval>
{
    private static readonly ValueConverter<string?, string?> JsonStringConverter = new(
        value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        value => value == null ? null : JsonSerializer.Deserialize<string>(value, (JsonSerializerOptions?)null));

    public void Configure(EntityTypeBuilder<AgentApproval> builder)
    {
        builder.ToTable("agent_approval");
        builder.HasKey(x => x.ApprovalId);

        builder.Property(x => x.ApprovalId).HasColumnName("approval_id").HasMaxLength(64);
        builder.Property(x => x.RunId).HasColumnName("run_id").HasMaxLength(64);
        builder.Property(x => x.StepId).HasColumnName("step_id").HasMaxLength(64);
        builder.Property(x => x.RequestedAction).HasColumnName("requested_action").HasMaxLength(256);
        builder.Property(x => x.RiskLevel).HasColumnName("risk_level").HasMaxLength(32);
        builder.Property(x => x.RequestPayload).HasColumnName("request_payload").HasColumnType("jsonb").HasConversion(JsonStringConverter);
        builder.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(32);
        builder.Property(x => x.ApproverId).HasColumnName("approver_id").HasMaxLength(64);
        builder.Property(x => x.ApproverRole).HasColumnName("approver_role").HasMaxLength(64);
        builder.Property(x => x.ApproverName).HasColumnName("approver_name").HasMaxLength(128);
        builder.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(512);
        builder.Property(x => x.WaitToken).HasColumnName("wait_token").HasMaxLength(128);
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(EntityTypeBuilder<AgentApproval> builder)
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
