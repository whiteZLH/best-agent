using BestAgent.Application;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunApprovals;
using BestAgent.Domain.AgentRuns;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Queries.GetAgentRunApprovals;

public class GetAgentRunApprovalsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnRunApprovals()
    {
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(approvalRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc);

        approvalRepository.ListByRunIdAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(
            [
                new AgentApproval
                {
                    ApprovalId = "approval-1",
                    RunId = "run-1",
                    StepId = "step-1",
                    RequestedAction = "weather",
                    RiskLevel = "internal_write",
                    RequestPayload = "{\"city\":\"Shanghai\",\"apiKey\":\"secret-1\"}",
                    Decision = "Approved",
                    ApproverId = "u-1",
                    ApproverName = "Alice",
                    ApproverRole = "admin",
                    Comment = "Looks good",
                    WaitToken = "wait-1",
                    DecidedAt = now,
                    CreateTime = now,
                    LastModifyTime = now
                }
            ]);

        var result = await mediator.Send(new GetAgentRunApprovalsQuery("run-1"));

        var approval = Assert.Single(result);
        Assert.Equal("approval-1", approval.ApprovalId);
        Assert.Equal("weather", approval.RequestedAction);
        Assert.Equal("Approved", approval.Decision);
        Assert.Equal("Alice", approval.ApproverName);
        Assert.Equal("{\"city\":\"Shanghai\",\"apiKey\":\"***\"}", approval.RequestPayload);
    }
}
