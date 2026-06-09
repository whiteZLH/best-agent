namespace BestAgent.Application.Models;

public sealed record GenerateTextMessageInputAudioPart(
    string Data,
    string Format)
    : GenerateTextMessageContentPart("input_audio");
