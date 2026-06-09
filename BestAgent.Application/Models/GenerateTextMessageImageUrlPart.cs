namespace BestAgent.Application.Models;

public sealed record GenerateTextMessageImageUrlPart(
    string Url,
    string? Detail = null)
    : GenerateTextMessageContentPart("image_url");
