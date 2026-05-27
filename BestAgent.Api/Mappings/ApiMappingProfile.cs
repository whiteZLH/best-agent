using BestAgent.Api.Contracts.AgentDefinitions;
using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Api.Contracts.Tools;
using BestAgent.Application.AgentDefinitions;
using BestAgent.Application.AgentDefinitions.Commands.ActivateAgentDefinitionVersion;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinition;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinitionVersion;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Commands.ResumeAgentRun;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;
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
                src.MaxTurns,
                src.MaxCost));
        CreateMap<AgentDefinitionViewModel, GetAgentDefinitionResponse>();
        CreateMap<AgentDefinitionVersionViewModel, GetAgentDefinitionVersionResponse>();
        CreateMap<CreateAgentRunRequest, CreateAgentRunCommand>();
        CreateMap<CreateAgentRunResult, CreateAgentRunResponse>();
        CreateMap<ResumeAgentRunResult, ResumeAgentRunResponse>();
        CreateMap<GetAgentRunByIdResult, GetAgentRunResponse>();
        CreateMap<GetAgentRunStepsItem, GetAgentRunStepResponse>();
        CreateMap<CreateToolDefinitionRequest, CreateToolDefinitionCommand>();
        CreateMap<ToolDefinitionViewModel, GetToolDefinitionResponse>();
    }
}
