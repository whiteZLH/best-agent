namespace BestAgent.Domain.Common;

public abstract record class AuditedEntity
{
    public string LastModifier { get; init; } = string.Empty;

    public DateTime LastModifyTime { get; init; }

    public string LastModifierName { get; init; } = string.Empty;

    public DateTime CreateTime { get; init; }

    public string CreatorName { get; init; } = string.Empty;

    public string Creator { get; init; } = string.Empty;

    public bool Deleted { get; init; }
}
