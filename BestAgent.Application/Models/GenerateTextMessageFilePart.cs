namespace BestAgent.Application.Models;

public sealed record GenerateTextMessageFilePart(
    string? FileId = null,
    string? FileData = null,
    string? FileName = null)
    : GenerateTextMessageContentPart("file");
