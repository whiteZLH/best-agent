namespace BestAgent.Api.Contracts.AgentRuns;

public record CompleteApprovalRequest(
    string Decision,
    string? ApproverId = null,
    string? ApproverName = null,
    string? ApproverRole = null,
    string? Comment = null);
