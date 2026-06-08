using BestAgent.Api.Infrastructure;
using BestAgent.Api.Mappings;
using BestAgent.Application;
using BestAgent.Application.AgentRuns.Approvals;
using BestAgent.Application.AgentRuns.Commands.CreateAgentRun;
using BestAgent.Infrastructure;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

var approvalPolicyOptions = new ApprovalPolicyOptions
{
    ApprovalRequiredSideEffectLevels = ReadStringList(
        builder.Configuration,
        "Approval:Policy:ApprovalRequiredSideEffectLevels",
        ApprovalPolicyOptions.DefaultApprovalRequiredSideEffectLevels),
    RoleRequiredSideEffectLevels = ReadStringList(
        builder.Configuration,
        "Approval:Policy:RoleRequiredSideEffectLevels",
        ApprovalPolicyOptions.DefaultRoleRequiredSideEffectLevels),
    AllowedApproverRoles = ReadStringList(
        builder.Configuration,
        "Approval:Policy:AllowedApproverRoles",
        ApprovalPolicyOptions.DefaultAllowedApproverRoles),
    ParameterApprovalRules = ReadApprovalParameterRules(
        builder.Configuration,
        "Approval:Policy:ParameterApprovalRules")
};
var humanTakeoverPolicyOptions = new HumanTakeoverPolicyOptions
{
    AllowedHumanOperatorRoles = ReadStringList(
        builder.Configuration,
        "HumanTakeover:AllowedRoles",
        Array.Empty<string>())
};
var tenantApprovalPolicyOptions = ReadTenantApprovalPolicyOptions(
    builder.Configuration,
    "Approval:TenantPolicies");

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddSingleton(new BestAgentAuthenticationOptions
{
    Users = ReadAuthenticationUsers(builder.Configuration, "Authentication:Users"),
    Services = ReadAuthenticationServices(builder.Configuration, "Authentication:Services"),
    RequireAuthenticatedRunAccess = bool.TryParse(
        builder.Configuration["Authentication:RequireAuthenticatedRunAccess"],
        out var requireAuthenticatedRunAccess)
        && requireAuthenticatedRunAccess,
    RequireAuthenticatedManagementAccess = bool.TryParse(
        builder.Configuration["Authentication:RequireAuthenticatedManagementAccess"],
        out var requireAuthenticatedManagementAccess)
        && requireAuthenticatedManagementAccess,
    ManagementAllowedRoles = ReadStringList(
        builder.Configuration,
        "Authentication:ManagementAllowedRoles",
        Array.Empty<string>())
});
builder.Services
    .AddAuthentication(BestAgentAuthenticationOptions.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BestAgentAuthenticationHandler>(
        BestAgentAuthenticationOptions.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddSingleton(new WebhookSecurityOptions
{
    RequireSignature = bool.TryParse(builder.Configuration["WebhookSecurity:RequireSignature"], out var requireSignature) && requireSignature,
    ToolCallbackSecret = builder.Configuration["WebhookSecurity:ToolCallbackSecret"] ?? string.Empty,
    ApprovalCallbackSecret = builder.Configuration["WebhookSecurity:ApprovalCallbackSecret"] ?? string.Empty,
    ApprovalCallbackSecrets = ReadStringList(
        builder.Configuration,
        "WebhookSecurity:ApprovalCallbackSecrets",
        Array.Empty<string>()),
    SignatureHeaderName = string.IsNullOrWhiteSpace(builder.Configuration["WebhookSecurity:SignatureHeaderName"])
        ? "X-BestAgent-Signature"
        : builder.Configuration["WebhookSecurity:SignatureHeaderName"]!
});
builder.Services.AddSingleton<IWebhookRequestAuthorizer, HmacWebhookRequestAuthorizer>();
builder.Services.AddApplication(
    approvalPolicyOptions,
    humanTakeoverPolicyOptions,
    tenantApprovalPolicyOptions);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAutoMapper(
    _ => { },
    typeof(ApiMappingProfile).Assembly,
    typeof(CreateAgentRunMappingProfile).Assembly);

var app = builder.Build();

app.UseMiddleware<BestAgentRequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<BestAgentAuthenticationEnforcementMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();

static string[] ReadStringList(
    IConfiguration configuration,
    string sectionPath,
    IReadOnlyList<string> fallback)
{
    var section = configuration.GetSection(sectionPath);
    var values = new List<string>();

    if (!string.IsNullOrWhiteSpace(section.Value))
    {
        values.AddRange(section.Value
            .Split([',', ';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    foreach (var child in section.GetChildren())
    {
        if (!string.IsNullOrWhiteSpace(child.Value))
        {
            values.Add(child.Value.Trim());
        }
    }

    var normalized = values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return normalized.Length > 0
        ? normalized
        : fallback.ToArray();
}

static ApprovalParameterRule[] ReadApprovalParameterRules(
    IConfiguration configuration,
    string sectionPath)
{
    return configuration
        .GetSection(sectionPath)
        .GetChildren()
        .Select(section => new ApprovalParameterRule
        {
            ToolName = section["ToolName"] ?? string.Empty,
            InputPath = section["InputPath"] ?? string.Empty,
            ExpectedValue = section["ExpectedValue"],
            OverrideSideEffectLevel = section["OverrideSideEffectLevel"]
        })
        .Where(rule =>
            !string.IsNullOrWhiteSpace(rule.ToolName)
            && !string.IsNullOrWhiteSpace(rule.InputPath))
        .ToArray();
}

static TenantApprovalPolicyOptions ReadTenantApprovalPolicyOptions(
    IConfiguration configuration,
    string sectionPath)
{
    var policies = new Dictionary<string, ApprovalPolicyOptions>(StringComparer.OrdinalIgnoreCase);
    foreach (var section in configuration.GetSection(sectionPath).GetChildren())
    {
        var tenantId = NormalizeOptional(section["TenantId"]);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            continue;
        }

        policies[tenantId] = new ApprovalPolicyOptions
        {
            ApprovalRequiredSideEffectLevels = ReadStringList(
                section,
                "ApprovalRequiredSideEffectLevels",
                Array.Empty<string>()),
            RoleRequiredSideEffectLevels = ReadStringList(
                section,
                "RoleRequiredSideEffectLevels",
                Array.Empty<string>()),
            AllowedApproverRoles = ReadStringList(
                section,
                "AllowedApproverRoles",
                Array.Empty<string>()),
            ParameterApprovalRules = ReadApprovalParameterRules(
                section,
                "ParameterApprovalRules")
        };
    }

    return new TenantApprovalPolicyOptions
    {
        PoliciesByTenantId = policies
    };
}

static BestAgentAuthenticatedUser[] ReadAuthenticationUsers(
    IConfiguration configuration,
    string sectionPath)
{
    return configuration
        .GetSection(sectionPath)
        .GetChildren()
        .Select(section => new BestAgentAuthenticatedUser(
            section["Token"]?.Trim() ?? string.Empty,
            section["UserId"]?.Trim() ?? string.Empty,
            section["UserName"]?.Trim() ?? string.Empty,
            ReadStringList(section, "Roles", Array.Empty<string>()),
            NormalizeOptional(section["TenantId"]),
            NormalizeOptional(section["SessionId"])))
        .Where(user =>
            !string.IsNullOrWhiteSpace(user.Token)
            && !string.IsNullOrWhiteSpace(user.UserId))
        .ToArray();
}

static BestAgentAuthenticatedService[] ReadAuthenticationServices(
    IConfiguration configuration,
    string sectionPath)
{
    return configuration
        .GetSection(sectionPath)
        .GetChildren()
        .Select(section => new BestAgentAuthenticatedService(
            section["Token"]?.Trim() ?? string.Empty,
            section["ServiceId"]?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(section["DisplayName"])
                ? section["ServiceId"]?.Trim() ?? string.Empty
                : section["DisplayName"]!.Trim(),
            ReadStringList(section, "Roles", ["service"])))
        .Where(service =>
            !string.IsNullOrWhiteSpace(service.Token)
            && !string.IsNullOrWhiteSpace(service.ServiceId))
        .ToArray();
}

static string? NormalizeOptional(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
