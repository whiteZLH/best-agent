using System.Diagnostics;

namespace BestAgent.Application.Observability;

public static class AgentTracing
{
    public const string SourceName = "BestAgent.Runtime";
    public const string RunProcessActivityName = "bestagent.run.process";
    public const string ModelCallActivityName = "bestagent.model.call";
    public const string ToolExecutionActivityName = "bestagent.tool.execute";
    public const string RetrievalActivityName = "bestagent.retrieval";
    public const string ApprovalActivityName = "bestagent.approval";
    public const string HandoffActivityName = "bestagent.handoff";
    public const string RunStreamActivityName = "bestagent.run.stream";

    public static readonly ActivitySource Source = new(SourceName);
}
