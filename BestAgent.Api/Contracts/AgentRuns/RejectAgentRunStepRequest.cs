namespace BestAgent.Api.Contracts.AgentRuns;

public record RejectAgentRunStepRequest(
    string? Comment,
    string? ApproverId = null,
    string? ApproverName = null,
    string? ApproverRole = null);
