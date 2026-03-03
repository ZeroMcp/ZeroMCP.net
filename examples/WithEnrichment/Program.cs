using ZeroMCP.Extensions;
using ZeroMCP.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddZeroMcp(options =>
{
    options.ServerName = "WithEnrichment Example";
    options.ServerVersion = "1.0.0";
    options.RoutePrefix = "/mcp";
    options.EnableResultEnrichment = true;
    options.EnableSuggestedFollowUps = true;
    options.EnableStreamingToolResults = true;
    options.StreamingChunkSize = 1024;
    options.ResponseHintProvider = (toolName, statusCode, content, isError, _) =>
    {
        if (isError) return new[] { "Consider retrying or checking arguments." };
        if (statusCode >= 400) return new[] { "Request failed; check tool arguments." };
        return null;
    };
    options.SuggestedFollowUpsProvider = (toolName, statusCode, _, isError, _) =>
    {
        if (isError) return null;
        return toolName switch
        {
            "get_catalog" => new[] { new ZeroMCPOptionsSuggestedAction("get_item", "Fetch a specific item by id from the catalog.") },
            "get_item" => new[] { new ZeroMCPOptionsSuggestedAction("get_catalog", "List all catalog items.") },
            _ => null
        };
    };
});

var app = builder.Build();

app.UseRouting();
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .AsMcp("health_check", "Health check.", category: "system");

app.MapZeroMcp();

app.Run();
