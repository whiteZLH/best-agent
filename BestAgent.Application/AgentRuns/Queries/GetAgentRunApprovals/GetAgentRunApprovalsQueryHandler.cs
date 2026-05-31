using MediatR;
using BestAgent.Domain.AgentRuns;

namespace BestAgent.Application.AgentRuns.Queries.GetAgentRunApprovals;

public class GetAgentRunApprovalsQueryHandler : IRequestHandler<GetAgentRunApprovalsQuery, IReadOnlyList<GetAgentRunApprovalsItem>>
{
    private readonly IAgentApprovalRepository _agentApprovalRepository;

    public GetAgentRunApprovalsQueryHandler(IAgentApprovalRepository agentApprovalRepository)
    {
        _agentApprovalRepository = agentApprovalRepository;
    }

    public async Task<IReadOnlyList<GetAgentRunApprovalsItem>> Handle(GetAgentRunApprovalsQuery request, CancellationToken cancellationToken)
    {
        var approvals = await _agentApprovalRepository.ListByRunIdAsync(request.RunId, cancellationToken);
        return approvals
            .Select(GetAgentRunApprovalsItem.FromEntity)
            .ToArray();
    }
}
