using BestAgent.Domain.AgentDefinitions;
using BestAgent.Domain.AgentRuns;

namespace BestAgent.Application.AgentRuns.Runtime;

public record AgentLoopContext(
    AgentRun Run,
    AgentDefinitionVersion Version,
    string CurrentInput,
    int StartStepNo,
    int StartTurn);
