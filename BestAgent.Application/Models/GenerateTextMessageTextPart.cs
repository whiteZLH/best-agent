namespace BestAgent.Application.Models;

public sealed record GenerateTextMessageTextPart(string Text)
    : GenerateTextMessageContentPart("text");
