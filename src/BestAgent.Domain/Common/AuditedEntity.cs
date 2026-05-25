namespace BestAgent.Domain.Common;

public abstract class AuditedEntity
{
    public string LastModifier { get; set; } = string.Empty;

    public DateTimeOffset LastModifyTime { get; set; }

    public string LastModifierName { get; set; } = string.Empty;

    public DateTimeOffset CreateTime { get; set; }

    public string CreatorName { get; set; } = string.Empty;

    public string Creator { get; set; } = string.Empty;

    public bool Deleted { get; set; }
}
