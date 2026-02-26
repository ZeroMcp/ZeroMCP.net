using Microsoft.AspNetCore.Mvc;
using SampleApi;
using ZeroMCP.Attributes;

namespace SampleApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductController : ControllerBase
{
    [HttpGet]
    [Mcp("list_products", Description = "Lists all products.")]
    public ActionResult<IEnumerable<Product>> List()
    {
        return Ok(SampleData.Products);
    }

    [HttpGet("{id:int}")]
    [Mcp("get_product", Description = "Retrieves a single product by ID.")]
    public ActionResult<Product> Get(int id)
    {
        var product = SampleData.Products.FirstOrDefault(p => p.Id == id);
        return product is null ? NotFound($"Product {id} not found") : Ok(product);
    }

    [HttpPost]
    [Mcp("create_product", Description = "Creates a new product. Returns the created product with assigned ID.", Tags = ["write"])]
    public ActionResult<Product> Create([FromBody] CreateProductRequest request)
    {
        var id = SampleData.Products.Count > 0 ? SampleData.Products.Max(p => p.Id) + 1 : 1;
        var product = new Product
        {
            Id = id,
            Name = request.Name,
            Price = request.Price
        };
        SampleData.Products.Add(product);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }
}

public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
