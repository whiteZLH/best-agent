using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Runtime;
using BestAgent.Application.Observability;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BestAgent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, ApprovalPolicyOptions? approvalPolicyOptions = null)
    {
        var normalizedApprovalPolicyOptions = ApprovalPolicyOptionsNormalizer.Normalize(approvalPolicyOptions);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddSingleton<IStepDecisionParser, StepDecisionParser>();
        services.AddSingleton<IAgentRunChannel, AgentRunChannel>();
        services.AddSingleton<IAgentRunEventBus, AgentRunEventBus>();
        services.TryAddSingleton(normalizedApprovalPolicyOptions);
        services.TryAddSingleton<IAgentMetrics>(NullAgentMetrics.Instance);
        services.AddSingleton<IApprovalAuthorizer, DefaultApprovalAuthorizer>();
        services.AddSingleton<IHumanTakeoverAuthorizer, DefaultHumanTakeoverAuthorizer>();
        services.AddSingleton<IRuntimeContextComposer, PassThroughRuntimeContextComposer>();
        services.AddSingleton<IRuntimeMemoryWriter, NoOpRuntimeMemoryWriter>();
        return services;
    }
}
