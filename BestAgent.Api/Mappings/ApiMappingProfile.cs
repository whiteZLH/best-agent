using AutoMapper;
using BestAgent.Api.Contracts.AgentRuns;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;

namespace BestAgent.Api.Mappings;

public class ApiMappingProfile : Profile
{
    public ApiMappingProfile()
    {
        CreateMap<CreateAgentRunRequest, CreateAgentRunCommand>();
        CreateMap<CreateAgentRunResult, CreateAgentRunResponse>();
    }
}
