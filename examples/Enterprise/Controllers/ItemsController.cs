using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZeroMcp.Attributes;

namespace EnterpriseExample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ItemsController : ControllerBase
{
    private static readonly List<Item> Store = new() { new Item(1, "Item1"), new Item(2, "Item2") };

    [HttpGet]
    [Mcp("list_items", Description = "List all items.", Category = "items")]
    public IActionResult List()
    {
        return Ok(Store);
    }

    [HttpGet("{id:int}")]
    [Mcp("get_item_by_id", Description = "Get item by id.", Category = "items")]
    public IActionResult Get(int id)
    {
        var item = Store.Find(i => i.Id == id);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    [Authorize]
    [Mcp("create_item", Description = "Create a new item. Requires auth.", Category = "items")]
    public IActionResult Create([FromBody] CreateItemRequest req)
    {
        var item = new Item(Store.Count + 1, req.Name);
        Store.Add(item);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    [Mcp("admin_delete_item", Description = "Delete item. Admin only.", Category = "items", Roles = ["Admin"])]
    public IActionResult Delete(int id)
    {
        var removed = Store.RemoveAll(i => i.Id == id);
        return removed > 0 ? NoContent() : NotFound();
    }
}

public record Item(int Id, string Name);
public record CreateItemRequest(string Name);
