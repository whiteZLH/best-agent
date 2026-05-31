namespace BestAgent.Api.Contracts.AgentRuns;

public record ApproveAgentRunStepRequest(
    string? ApproverId = null,
    string? ApproverName = null,
    string? ApproverRole = null,
    string? Comment = null);
