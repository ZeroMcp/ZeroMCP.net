using Microsoft.AspNetCore.Authentication;
using WithAuthExample.Auth;
using ZeroMCP.Extensions;

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
    options.ServerName = "WithAuth Example";
    options.ServerVersion = "1.0.0";
    options.RoutePrefix = "/mcp";
});

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .AsMcp("health_check", "Public health check.");

app.MapGet("/api/admin/health", () => Results.Ok(new { status = "admin-ok" }))
   .AsMcp("admin_health", "Admin-only health. Visible only when user has Admin role.", roles: new[] { "Admin" });

app.MapZeroMcp();

app.Run();
