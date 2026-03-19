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
    options.EnableListChangedNotifications = true;
    options.EnableResourceSubscriptions = true;
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

// ---------------------------------------------------------------------------
// Minimal API: MCP Resources — demonstrate .AsResource() and .AsTemplate()
// ---------------------------------------------------------------------------

// Static resource: a simple system status document at a well-known URI
app.MapGet("/api/system/status", () => Results.Ok(new
{
    environment = app.Environment.EnvironmentName,
    utcTime = DateTime.UtcNow,
    status = "ok"
})).AsResource(
    "system://status",
    "system_status",
    "Current system status and environment information.",
    mimeType: "application/json");

// Resource template: look up any order by id using a custom URI scheme
app.MapGet("/api/orders/resource/{id:int}", (int id) =>
{
    var order = SampleData.Orders.FirstOrDefault(o => o.Id == id);
    return order is null ? Results.NotFound($"Order {id} not found.") : Results.Ok(order);
}).AsTemplate(
    "orders://order/{id}",
    "order_resource",
    "Retrieves a single order by numeric ID via the orders:// URI scheme.",
    mimeType: "application/json");

// ---------------------------------------------------------------------------
// Minimal API: Demo notify-change — triggers notifications/resources/updated
// for clients subscribed to an order resource URI.
// ---------------------------------------------------------------------------
app.MapPost("/api/orders/resource/{id:int}/notify-change", async (int id, ZeroMCP.Notifications.McpNotificationService notificationService) =>
{
    var uri = $"orders://order/{id}";
    await notificationService.NotifyResourceUpdatedAsync(uri);
    return Results.Ok(new { notified = uri });
});

// ---------------------------------------------------------------------------
// Minimal API: MCP Prompt — demonstrate .AsPrompt()
// ---------------------------------------------------------------------------

// Prompt: generate a fulfilment chase message for an order
app.MapGet("/api/prompts/fulfil/{orderId:int}", (int orderId, string? urgency) =>
{
    var order = SampleData.Orders.FirstOrDefault(o => o.Id == orderId);
    if (order is null) return Results.NotFound($"Order {orderId} not found.");

    var urgencyClause = urgency is null ? "" : $" This is {urgency} priority.";
    var prompt = $"""
        You are a customer-service agent. Draft a polite fulfilment-chase email for the following order:{urgencyClause}

        Order ID : {order.Id}
        Customer : {order.CustomerName}
        Product  : {order.Product}
        Quantity : {order.Quantity}
        Status   : {order.Status}

        Keep the email under 120 words and end with a specific action request.
        """;
    return Results.Ok(prompt);
}).AsPrompt(
    "fulfil_order_prompt",
    "Generates a customer-service fulfilment-chase email prompt for a given order.");

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
