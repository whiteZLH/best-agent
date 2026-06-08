namespace BestAgent.Api.Infrastructure;

public sealed class WebhookSecurityOptions
{
    public bool RequireSignature { get; init; }

    public string ToolCallbackSecret { get; init; } = string.Empty;

    public string ApprovalCallbackSecret { get; init; } = string.Empty;

    public IReadOnlyList<string> ApprovalCallbackSecrets { get; init; } = Array.Empty<string>();

    public string SignatureHeaderName { get; init; } = "X-BestAgent-Signature";
}
