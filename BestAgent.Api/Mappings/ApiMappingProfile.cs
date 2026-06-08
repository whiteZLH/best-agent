using BestAgent.Api.Contracts.AgentDefinitions;
using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Api.Contracts.Tools;
using BestAgent.Application.AgentDefinitions;
using BestAgent.Application.AgentDefinitions.Commands.ActivateAgentDefinitionVersion;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinition;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinitionVersion;
using BestAgent.Application.AgentDefinitions.Commands.CreateRouteRule;
using BestAgent.Application.AgentDefinitions.Queries.GetRouteRules;
using BestAgent.Application.AgentRuns.Commands.CancelAgentRun;
using BestAgent.Application.AgentRuns.Commands.ApproveAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.CompleteHumanAgentRun;
using BestAgent.Application.AgentRuns.Commands.CompleteApproval;
using BestAgent.Application.AgentRuns.Commands.CompleteToolInvocation;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Commands.RequestHumanAgentRun;
using BestAgent.Application.AgentRuns.Commands.RejectAgentRunStep;
using BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunApprovals;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunChildren;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunEvents;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunTree;
using BestAgent.Application.Tools;
using BestAgent.Application.Tools.Commands.CreateToolDefinition;

namespace BestAgent.Api.Mappings;

public class ApiMappingProfile : Profile
{
    public ApiMappingProfile()
    {
        CreateMap<ActivateAgentDefinitionVersionRequest, ActivateAgentDefinitionVersionCommand>();
        CreateMap<CreateAgentDefinitionRequest, CreateAgentDefinitionCommand>();
        CreateMap<CreateAgentDefinitionVersionRequest, CreateAgentDefinitionVersionCommand>()
            .ConstructUsing(src => new CreateAgentDefinitionVersionCommand(
                string.Empty,
                src.Name,
                src.Description,
                src.Instruction,
                src.SystemPromptTemplate,
                src.DefaultModel,
                src.AllowedTools,
                src.KnowledgeSources,
                src.MemoryPolicy,
                src.RoutingPolicy,
                src.ApprovalPolicy,
                src.ExecutionPolicy,
                src.PlannerPolicy,
                src.ContextPolicy,
                src.AllowedHandoffs,
                src.OutputSchema,
                src.MaxTurns,
                src.MaxCost,
                src.DeniedTools));
        CreateMap<CreateRouteRuleRequest, CreateRouteRuleCommand>()
            .ConstructUsing(src => new CreateRouteRuleCommand(
                string.Empty,
                0,
                src.TargetAgentCode,
                src.RuleName,
                src.Priority,
                src.MatchType,
                src.MatchExpression,
                src.HandoffMode,
                src.MergeStrategy,
                src.ContextScope,
                src.MemoryScope,
                src.ToolScope,
                src.KnowledgeScope,
                src.ApprovalRequired,
                src.Enabled));
        CreateMap<AgentDefinitionViewModel, GetAgentDefinitionResponse>();
        CreateMap<AgentDefinitionVersionViewModel, GetAgentDefinitionVersionResponse>();
        CreateMap<RouteRuleViewModel, GetRouteRuleResponse>();
        CreateMap<CreateAgentRunRequest, CreateAgentRunCommand>()
            .ConstructUsing(src => new CreateAgentRunCommand(
                src.AgentCode,
                src.Input,
                src.IdempotencyKey,
                src.TenantId,
                src.UserId,
                src.SessionId,
                src.Options == null ? null : src.Options.Stream,
                src.Options == null ? null : src.Options.MaxRounds));
        CreateMap<CreateAgentRunResult, CreateAgentRunResponse>();
        CreateMap<CancelAgentRunResult, CancelAgentRunResponse>();
        CreateMap<CompleteApprovalResult, CompleteApprovalResponse>();
        CreateMap<CompleteHumanAgentRunResult, CompleteHumanAgentRunResponse>();
        CreateMap<CompleteToolInvocationResult, CompleteToolInvocationResponse>();
        CreateMap<ApproveAgentRunStepResult, ApproveAgentRunStepResponse>();
        CreateMap<RejectAgentRunStepResult, RejectAgentRunStepResponse>();
        CreateMap<RequestHumanAgentRunResult, RequestHumanAgentRunResponse>();
        CreateMap<ResumeAgentRunResult, ResumeAgentRunResponse>();
        CreateMap<GetAgentRunByIdResult, GetAgentRunResponse>();
        CreateMap<GetAgentRunChildrenItem, GetAgentRunChildResponse>();
        CreateMap<GetAgentRunTreeItem, GetAgentRunTreeResponse>();
        CreateMap<HandoffInfo, HandoffInfoResponse>();
        CreateMap<ApprovalInfo, ApprovalInfoResponse>();
        CreateMap<HumanWaitInfo, HumanWaitInfoResponse>();
        CreateMap<ToolInvocationInfo, ToolInvocationInfoResponse>();
        CreateMap<ModelCallToolCallInfo, ModelCallToolCallInfoResponse>();
        CreateMap<ModelCallRetrievalInfo, ModelCallRetrievalInfoResponse>();
        CreateMap<ModelCallInfo, ModelCallInfoResponse>();
        CreateMap<RetrievalInfo, RetrievalInfoResponse>();
        CreateMap<ModelFailureInfo, ModelFailureInfoResponse>();
        CreateMap<ToolFailureCompensationInfo, ToolFailureCompensationInfoResponse>();
        CreateMap<ToolFailureInfo, ToolFailureInfoResponse>();
        CreateMap<EventModelCallToolCallInfo, EventModelCallToolCallInfoResponse>();
        CreateMap<EventModelCallRetrievalInfo, EventModelCallRetrievalInfoResponse>();
        CreateMap<EventModelCallInfo, EventModelCallInfoResponse>();
        CreateMap<EventRetrievalInfo, EventRetrievalInfoResponse>();
        CreateMap<GetAgentRunStepsItem, GetAgentRunStepResponse>();
        CreateMap<GetAgentRunApprovalsItem, GetAgentRunApprovalResponse>();
        CreateMap<EventModelFailureInfo, EventModelFailureInfoResponse>();
        CreateMap<EventToolFailureCompensationInfo, EventToolFailureCompensationInfoResponse>();
        CreateMap<EventToolFailureInfo, EventToolFailureInfoResponse>();
        CreateMap<EventToolInvocationInfo, EventToolInvocationInfoResponse>();
        CreateMap<EventApprovalInfo, EventApprovalInfoResponse>();
        CreateMap<EventHandoffInfo, EventHandoffInfoResponse>();
        CreateMap<EventHumanWaitInfo, EventHumanWaitInfoResponse>();
        CreateMap<EventDataInfo, EventDataInfoResponse>();
        CreateMap<GetAgentRunEventsItem, GetAgentRunEventResponse>();
        CreateMap<CreateToolDefinitionRequest, CreateToolDefinitionCommand>();
        CreateMap<ToolExecutionViewModel, ToolExecutionResponse>();
        CreateMap<WebhookToolExecutionViewModel, WebhookToolExecutionResponse>();
        CreateMap<LocalHandlerToolExecutionViewModel, LocalHandlerToolExecutionResponse>();
        CreateMap<InlineResultToolExecutionViewModel, InlineResultToolExecutionResponse>();
        CreateMap<ToolPoliciesViewModel, ToolPoliciesResponse>();
        CreateMap<RetryToolPolicyViewModel, RetryToolPolicyResponse>();
        CreateMap<AuthToolPolicyViewModel, AuthToolPolicyResponse>();
        CreateMap<ParameterToolPolicyViewModel, ParameterToolPolicyResponse>();
        CreateMap<IdempotencyToolPolicyViewModel, IdempotencyToolPolicyResponse>();
        CreateMap<CompensationToolPolicyViewModel, CompensationToolPolicyResponse>();
        CreateMap<ToolDefinitionViewModel, GetToolDefinitionResponse>();
    }
}
