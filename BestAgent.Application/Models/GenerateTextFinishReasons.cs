namespace BestAgent.Application.Models;

public static class GenerateTextFinishReasons
{
    public const string Completed = "completed";
    public const string ToolCall = "tool_call";
    public const string MaxOutputTokens = "max_output_tokens";
    public const string ContentFiltered = "content_filtered";
}
