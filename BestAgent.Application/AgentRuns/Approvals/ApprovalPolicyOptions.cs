namespace BestAgent.Application.AgentRuns.Approvals;

public sealed class ApprovalPolicyOptions
{
    public static readonly string[] DefaultApprovalRequiredSideEffectLevels =
    [
        "internal_write",
        "external_write",
        "destructive"
    ];

    public static readonly string[] DefaultRoleRequiredSideEffectLevels =
    [
        "internal_write",
        "external_write",
        "destructive"
    ];

    public static readonly string[] DefaultAllowedApproverRoles =
    [
        "approver",
        "reviewer",
        "admin",
        "owner"
    ];

    public IReadOnlyList<string> ApprovalRequiredSideEffectLevels { get; init; } = DefaultApprovalRequiredSideEffectLevels;

    public IReadOnlyList<string> RoleRequiredSideEffectLevels { get; init; } = DefaultRoleRequiredSideEffectLevels;

    public IReadOnlyList<string> AllowedApproverRoles { get; init; } = DefaultAllowedApproverRoles;

    public IReadOnlyList<ApprovalParameterRule> ParameterApprovalRules { get; init; } = [];
}
