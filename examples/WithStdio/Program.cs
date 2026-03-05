using ZeroMCP.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "Stdio Example";
    options.ServerVersion = "1.0.0";
    options.RoutePrefix = "/mcp";
});

var app = builder.Build();

app.UseRouting();
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }))
   .AsMcp("health_check", "Returns API health status.");

app.MapZeroMCP();

// stdio transport: when launched with --mcp-stdio, run JSON-RPC over stdin/stdout
// (Claude Desktop, Claude Code, VS Code extensions, etc.)
if (args.Contains("--mcp-stdio"))
{
    await app.RunMcpStdioAsync();
    return;
}

app.Run();
