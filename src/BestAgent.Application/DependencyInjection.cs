using System.Reflection;
using BestAgent.Application.Abstractions;
using BestAgent.Application.AgentRuns.Commands;
using BestAgent.Application.AgentRuns.Services;
using BestAgent.Application.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BestAgent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(assembly));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequestLoggingBehavior<,>));

        services.AddTransient<ContextBuilder>();
        services.AddTransient<PlanValidator>();
        services.AddTransient<AgentRuntimeService>();
        services.AddTransient<IRequestValidator<CreateAgentRunCommand>, CreateAgentRunCommandValidator>();
        services.AddTransient<IRequestValidator<ResumeAgentRunCommand>, ResumeAgentRunCommandValidator>();

        return services;
    }
}
