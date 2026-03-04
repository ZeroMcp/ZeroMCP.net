using Microsoft.AspNetCore.Authentication;
using SampleApi.Auth;
using ZeroMCP.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "Orders API";
    options.ServerVersion = "1.0.0";
    options.RoutePrefix = "/mcp";
    // Disable inspector/UI outside Development (e.g. production) if the tool list is sensitive
    var isDev = builder.Environment.IsDevelopment();
    options.EnableToolInspector = isDev;
    options.EnableToolInspectorUI = isDev;
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Minimal API example: exposed as MCP tool via WithMcpTool
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }))
   .AsMcp("health_check", "Returns API health status.", tags: new[] { "system" }, category: "system");

// Governance: role-based tool — only visible in tools/list when user is in Admin role
app.MapGet("/api/admin/health", () => Results.Ok(new { status = "admin-ok", timestamp = DateTime.UtcNow }))
   .AsMcp("admin_health", "Admin-only health. Visible only to callers with Admin role.", tags: new[] { "system", "admin" }, roles: new[] { "Admin" }, category: "admin");

app.MapZeroMCP();

app.Run();

// Expose for WebApplicationFactory<Program> in integration tests
public partial class Program { }
