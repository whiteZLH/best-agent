namespace BestAgent.Application.Planning;

public sealed record ModelContext(
    string AgentCode,
    string Instruction,
    IReadOnlyList<ModelMessage> Messages,
    string ModelName);
