using BestAgent.Api.Contracts.AgentDefinitions;
using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Application.AgentDefinitions;
using BestAgent.Application.AgentDefinitions.Commands.CreateAgentDefinition;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunById;
using BestAgent.Application.AgentRuns.Queries.GetAgentRunSteps;

namespace BestAgent.Api.Mappings;

public class ApiMappingProfile : Profile
{
    public ApiMappingProfile()
    {
        CreateMap<CreateAgentDefinitionRequest, CreateAgentDefinitionCommand>();
        CreateMap<AgentDefinitionViewModel, GetAgentDefinitionResponse>();
        CreateMap<CreateAgentRunRequest, CreateAgentRunCommand>();
        CreateMap<CreateAgentRunResult, CreateAgentRunResponse>();
        CreateMap<GetAgentRunByIdResult, GetAgentRunResponse>();
        CreateMap<GetAgentRunStepsItem, GetAgentRunStepResponse>();
    }
}
