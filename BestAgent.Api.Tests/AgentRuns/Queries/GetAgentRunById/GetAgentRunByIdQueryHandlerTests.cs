using BestAgent.Application;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Domain.AgentRuns;
using BestAgent.Domain.Tools;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BestAgent.Api.Tests.AgentRuns.Queries.GetAgentRunById;

public class GetAgentRunByIdQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnCurrentToolWaitPointers_WhenRunIsWaitingTool()
    {
        var runRepository = Substitute.For<IAgentRunRepository>();
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var toolInvocationRepository = Substitute.For<IToolInvocationRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(runRepository);
        services.AddSingleton(stepRepository);
        services.AddSingleton(approvalRepository);
        services.AddSingleton(toolInvocationRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var run = new AgentRun
        {
            RunId = "run-1",
            AgentCode = "writer",
            Status = "WaitingTool",
            InputPayload = "hello",
            CurrentStepNo = 4,
            CurrentWaitToken = "wait-1",
            CreateTime = now,
            LastModifyTime = now
        };
        var currentStep = new AgentStep
        {
            StepId = "step-4",
            RunId = run.RunId,
            StepNo = 4,
            StepType = "tool_call",
            Status = "Pending",
            CreateTime = now,
            LastModifyTime = now
        };
        var invocation = new ToolInvocation
        {
            InvocationId = "invocation-1",
            RunId = run.RunId,
            StepId = currentStep.StepId,
            ToolName = "weather",
            Status = "Pending",
            CallbackToken = "wait-1",
            CreateTime = now,
            LastModifyTime = now
        };

        runRepository.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        stepRepository.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(currentStep);
        toolInvocationRepository.GetPendingByRunIdAndStepIdAsync(run.RunId, currentStep.StepId, Arg.Any<CancellationToken>())
            .Returns(invocation);
        approvalRepository.GetByRunIdAndStepIdAsync(run.RunId, currentStep.StepId, Arg.Any<CancellationToken>())
            .Returns((AgentApproval?)null);

        var result = await mediator.Send(new GetAgentRunByIdQuery(run.RunId));

        Assert.NotNull(result);
        Assert.Equal("step-4", result!.CurrentStepId);
        Assert.Equal("tool_call", result.WaitStepType);
        Assert.Equal("invocation-1", result.CurrentInvocationId);
        Assert.Null(result.CurrentApprovalId);
    }

    [Fact]
    public async Task Handle_ShouldReturnCurrentApprovalPointers_WhenRunIsWaitingApproval()
    {
        var runRepository = Substitute.For<IAgentRunRepository>();
        var stepRepository = Substitute.For<IAgentStepRepository>();
        var approvalRepository = Substitute.For<IAgentApprovalRepository>();
        var toolInvocationRepository = Substitute.For<IToolInvocationRepository>();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(runRepository);
        services.AddSingleton(stepRepository);
        services.AddSingleton(approvalRepository);
        services.AddSingleton(toolInvocationRepository);
        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
        var run = new AgentRun
        {
            RunId = "run-2",
            AgentCode = "writer",
            Status = "WaitingApproval",
            InputPayload = "hello",
            CurrentStepNo = 5,
            CurrentWaitToken = "approval-wait-1",
            CreateTime = now,
            LastModifyTime = now
        };
        var currentStep = new AgentStep
        {
            StepId = "step-5",
            RunId = run.RunId,
            StepNo = 5,
            StepType = "approval_request",
            Status = "Pending",
            CreateTime = now,
            LastModifyTime = now
        };
        var approval = new AgentApproval
        {
            ApprovalId = "approval-1",
            RunId = run.RunId,
            StepId = currentStep.StepId,
            RequestedAction = "issue_refund",
            RiskLevel = "external_write",
            Decision = "Pending",
            WaitToken = "approval-wait-1",
            CreateTime = now,
            LastModifyTime = now
        };

        runRepository.GetByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        stepRepository.GetLastByRunIdAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(currentStep);
        toolInvocationRepository.GetPendingByRunIdAndStepIdAsync(run.RunId, currentStep.StepId, Arg.Any<CancellationToken>())
            .Returns((ToolInvocation?)null);
        approvalRepository.GetByRunIdAndStepIdAsync(run.RunId, currentStep.StepId, Arg.Any<CancellationToken>())
            .Returns(approval);

        var result = await mediator.Send(new GetAgentRunByIdQuery(run.RunId));

        Assert.NotNull(result);
        Assert.Equal("step-5", result!.CurrentStepId);
        Assert.Equal("approval_request", result.WaitStepType);
        Assert.Null(result.CurrentInvocationId);
        Assert.Equal("approval-1", result.CurrentApprovalId);
    }
}
