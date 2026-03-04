using Microsoft.AspNetCore.Authentication;
using EnterpriseExample.Auth;
using ZeroMcp.Extensions;
using ZeroMcp.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddZeroMcp(options =>
{
    options.ServerName = "Enterprise Example";
    options.ServerVersion = "1.0.0";
    options.RoutePrefix = "/mcp";
    options.CorrelationIdHeader = "X-Correlation-ID";
    options.EnableOpenTelemetryEnrichment = true;
    options.EnableResultEnrichment = true;
    options.EnableSuggestedFollowUps = true;
    options.EnableStreamingToolResults = true;
    options.StreamingChunkSize = 2048;
    options.ToolFilter = name => !name.StartsWith("internal_");
    options.ToolVisibilityFilter = (name, ctx) =>
    {
        if (name.StartsWith("admin_")) return ctx.User.IsInRole("Admin");
        return true;
    };
    options.ResponseHintProvider = (_, statusCode, _, isError, _) =>
        isError || statusCode >= 400 ? new[] { "Check request and retry if appropriate." } : null;
    options.SuggestedFollowUpsProvider = (toolName, _, _, isError, _) => isError ? null : Array.Empty<ZeroMCPOptionsSuggestedAction>();
});

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .AsMcp("health_check", "Public health.", tags: new[] { "system" });

app.MapGet("/api/admin/health", () => Results.Ok(new { status = "admin" }))
   .AsMcp("admin_health", "Admin health.", tags: new[] { "system", "admin" }, roles: new[] { "Admin" });

app.MapZeroMcp();

app.Run();
