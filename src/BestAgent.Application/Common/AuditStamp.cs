using BestAgent.Domain.Common;

namespace BestAgent.Application.Common;

public sealed record AuditStamp(
    string LastModifier,
    string LastModifierName,
    DateTimeOffset Timestamp,
    string Creator,
    string CreatorName)
{
    public static AuditStamp System(DateTimeOffset now)
    {
        return new AuditStamp("system", "system", now, "system", "system");
    }

    public void ApplyForCreate(AuditedEntity entity)
    {
        entity.LastModifier = LastModifier;
        entity.LastModifierName = LastModifierName;
        entity.LastModifyTime = Timestamp;
        entity.CreateTime = Timestamp;
        entity.Creator = Creator;
        entity.CreatorName = CreatorName;
        entity.Deleted = false;
    }

    public void ApplyForUpdate(AuditedEntity entity)
    {
        entity.LastModifier = LastModifier;
        entity.LastModifierName = LastModifierName;
        entity.LastModifyTime = Timestamp;
    }
}
