using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SampleApi;
using ZeroMCP.Attributes;

namespace SampleApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private static List<Order> Store => SampleData.Orders;

    [HttpGet("{id:int}")]
    [Mcp("get_order", Description = "Retrieves a single order by its numeric ID.")]
    public ActionResult<Order> GetOrder(int id)
    {
        var order = Store.FirstOrDefault(o => o.Id == id);
        return order is null ? NotFound($"Order {id} not found") : Ok(order);
    }

    [HttpGet("secure/{id:int}")]
    [Authorize]
    [Mcp("get_secure_order", Description = "Retrieves a single order by its numeric ID. Requires authentication.")]
    public ActionResult<Order> GetSecureOrder(int id)
    {
        var order = Store.FirstOrDefault(o => o.Id == id);
        return order is null ? NotFound($"Order {id} not found") : Ok(order);
    }

    [HttpGet]
    [Mcp("list_orders", Description = "Lists all orders. Optionally filter by status (pending, shipped, cancelled).")]
    public ActionResult<List<Order>> ListOrders([FromQuery] string? status = null)
    {
        var orders = status is null
            ? Store.ToList()
            : Store.Where(o => o.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
        return Ok(orders);
    }

    [HttpPost]
    [Mcp("create_order", Description = "Creates a new order. Returns the created order with its assigned ID.", Tags = ["write"])]
    public ActionResult<Order> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var order = new Order
        {
            Id = Store.Count + 1,
            CustomerName = request.CustomerName,
            Product = request.Product,
            Quantity = request.Quantity,
            Status = "pending"
        };
        Store.Add(order);
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    [HttpPatch("{id:int}/status")]
    [Mcp("update_order_status", Description = "Updates the status of an existing order.")]
    public ActionResult<Order> UpdateStatus(int id, [FromBody] UpdateStatusRequest request)
    {
        var order = Store.FirstOrDefault(o => o.Id == id);
        if (order is null) return NotFound($"Order {id} not found");
        order.Status = request.Status;
        return Ok(order);
    }

    [HttpDelete("{id:int}")]
    public IActionResult DeleteOrder(int id)
    {
        var order = Store.FirstOrDefault(o => o.Id == id);
        if (order is null) return NotFound();
        Store.Remove(order);
        return NoContent();
    }
}

public class Order
{
    public int Id { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = default!;
    public string Product { get; set; } = default!;
    public int Quantity { get; set; }
    public string Status { get; set; } = "pending";
}

public class CreateOrderRequest
{
    [Required][MinLength(1)]
    public string CustomerName { get; set; } = default!;
    [Required]
    public string Product { get; set; } = default!;
    [Range(1, 1000)]
    public int Quantity { get; set; } = 1;
}

public class UpdateStatusRequest
{
    [Required]
    [RegularExpression("^(pending|shipped|cancelled)$", ErrorMessage = "Status must be one of: pending, shipped, cancelled.")]
    public string Status { get; set; } = default!;
}
