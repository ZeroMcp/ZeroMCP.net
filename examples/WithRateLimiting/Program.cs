using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using ZeroMCP.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Option A: Use ASP.NET Core rate limiting. Define a policy and apply it to the MCP endpoint.
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("McpPolicy", config =>
    {
        config.PermitLimit = 10;                      // 10 requests
        config.Window = TimeSpan.FromSeconds(10);     // per 10 seconds
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;                        // no queue, reject immediately when over limit
    });
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            jsonrpc = "2.0",
            id = (object?)null,
            error = new
            {
                code = -32029,
                message = "Rate limit exceeded. Try again later."
            }
        }, ct);
    };
});

builder.Services.AddZeroMcp(options =>
{
    options.ServerName = "WithRateLimiting Example";
    options.ServerVersion = "1.0.0";
    options.RoutePrefix = "/mcp";
});

var app = builder.Build();

app.UseRouting();
app.UseRateLimiter();  // Must be after UseRouting for endpoint-level policies
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }))
   .AsMcp("health_check", "Returns API health status.");

// Apply the rate limiter to the MCP endpoint only (GET and POST /mcp)
app.MapZeroMcp().RequireRateLimiting("McpPolicy");

app.Run();
