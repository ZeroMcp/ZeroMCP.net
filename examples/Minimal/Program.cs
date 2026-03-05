using ZeroMCP.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "Minimal Example";
    options.ServerVersion = "1.0.0";
    options.RoutePrefix = "/mcp";
});

var app = builder.Build();

app.UseRouting();
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }))
   .AsMcp("health_check", "Returns API health status.");

// Priority 3: minimal API with query params
app.MapGet("/api/orders/minimal", (string? status, int page = 1, int pageSize = 20) =>
{
    var orders = new[] { new { id = 1, status = "pending" }, new { id = 2, status = "shipped" } };
    var filtered = status is null ? orders : orders.Where(o => string.Equals(o.status, status, StringComparison.OrdinalIgnoreCase)).ToArray();
    var skip = (page - 1) * pageSize;
    return Results.Ok(filtered.Skip(skip).Take(pageSize).ToArray());
}).AsMcp("list_orders_minimal", "Lists orders with optional status filter and pagination.");

app.MapZeroMCP();

app.Run();
