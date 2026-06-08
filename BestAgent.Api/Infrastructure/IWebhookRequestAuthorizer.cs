namespace BestAgent.Api.Infrastructure;

public interface IWebhookRequestAuthorizer
{
    Task AuthorizeToolCallbackAsync(HttpRequest request, string? callbackSecret, CancellationToken cancellationToken);

    Task AuthorizeApprovalCallbackAsync(HttpRequest request, CancellationToken cancellationToken);
}
