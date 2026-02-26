using Microsoft.AspNetCore.Mvc;
using SampleApi;
using ZeroMCP.Attributes;

namespace SampleApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CustomerController : ControllerBase
{
    [HttpGet]
    [Mcp("list_customers", Description = "Lists all customers.")]
    public ActionResult<IEnumerable<Customer>> List()
    {
        return Ok(SampleData.Customers);
    }

    [HttpGet("{id:int}")]
    [Mcp("get_customer", Description = "Retrieves a single customer by ID.")]
    public ActionResult<Customer> Get(int id)
    {
        var customer = SampleData.Customers.FirstOrDefault(c => c.Id == id);
        return customer is null ? NotFound($"Customer {id} not found") : Ok(customer);
    }

    [HttpGet("{id:int}/orders")]
    [Mcp("get_customer_orders", Description = "Returns all orders for the given customer ID. Uses nested route Customer/{id}/orders.")]
    public ActionResult<IEnumerable<Order>> GetOrders(int id)
    {
        var customer = SampleData.Customers.FirstOrDefault(c => c.Id == id);
        if (customer is null)
            return NotFound($"Customer {id} not found");
        var orders = SampleData.Orders.Where(o => o.CustomerId == id).ToList();
        return Ok(orders);
    }

    [HttpPost]
    [Mcp("create_customer", Description = "Creates a new customer. Returns the created customer with assigned ID.", Tags = ["write"])]
    public ActionResult<Customer> Create([FromBody] CreateCustomerRequest request)
    {
        var id = SampleData.Customers.Count > 0 ? SampleData.Customers.Max(c => c.Id) + 1 : 1;
        var customer = new Customer
        {
            Id = id,
            Name = request.Name,
            Email = request.Email
        };
        SampleData.Customers.Add(customer);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }
}

public class CreateCustomerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
