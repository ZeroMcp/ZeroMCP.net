using Microsoft.AspNetCore.Authentication;
using SampleApi;
using SampleApi.Auth;
using SampleApi.Controllers;
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

// Unversioned health (appears on all version endpoints)
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }))
   .AsMcp("health_check", "Returns API health status.", tags: new[] { "system" }, category: "system");
// Versioned health (v2 only) — same tool name, different implementation
app.MapGet("/api/v2/health", () => Results.Ok(new { status = "ok", version = "2", timestamp = DateTime.UtcNow }))
   .AsMcp("health_check", "Returns enhanced health status.", version: 2, category: "system");

// Governance: role-based tool — only visible in tools/list when user is in Admin role
app.MapGet("/api/admin/health", () => Results.Ok(new { status = "admin-ok", timestamp = DateTime.UtcNow }))
   .AsMcp("admin_health", "Admin-only health. Visible only to callers with Admin role.", tags: new[] { "system", "admin" }, roles: new[] { "Admin" }, category: "admin");

// Priority 3: Minimal API binding parity — query params, [FromBody] with validation
app.MapGet("/api/orders/minimal", (string? status, int page = 1, int pageSize = 20) =>
{
    var orders = status is null
        ? SampleData.Orders.ToList()
        : SampleData.Orders.Where(o => o.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
    var skip = (page - 1) * pageSize;
    var paged = orders.Skip(skip).Take(pageSize).ToList();
    return Results.Ok(paged);
}).AsMcp("list_orders_minimal", "Lists orders with optional status filter and pagination (query params).");

app.MapPost("/api/orders/minimal", (CreateOrderRequest req) =>
{
    var order = new Order
    {
        Id = SampleData.Orders.Count + 1,
        CustomerName = req.CustomerName,
        Product = req.Product,
        Quantity = req.Quantity,
        Status = "pending"
    };
    SampleData.Orders.Add(order);
    return Results.Created($"/api/orders/{order.Id}", order);
}).AsMcp("create_order_minimal", "Creates a new order. Returns the created order with its assigned ID.", tags: new[] { "write" });

app.MapZeroMCP().WithLegacySseTransport();

// stdio transport: when launched with --mcp-stdio, run JSON-RPC over stdin/stdout (Claude Desktop, Claude Code)
if (args.Contains("--mcp-stdio"))
{
    await app.RunMcpStdioAsync();
    return;
}

app.Run();

// Expose for WebApplicationFactory<Program> in integration tests
public partial class Program { }
