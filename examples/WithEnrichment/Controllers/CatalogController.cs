using Microsoft.AspNetCore.Mvc;
using ZeroMCP.Attributes;

namespace WithEnrichmentExample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CatalogController : ControllerBase
{
    private static readonly List<CatalogItem> Items = new()
    {
        new CatalogItem(1, "Widget A"),
        new CatalogItem(2, "Widget B")
    };

    [HttpGet]
    [Mcp("get_catalog",
        Description = "Returns the full catalog of items.",
        Category = "catalog",
        Examples = ["Use when the user asks for all items or to browse the catalog."],
        Hints = ["Response includes id and name for each item."])]
    public IActionResult GetAll()
    {
        return Ok(Items);
    }

    [HttpGet("{id:int}")]
    [Mcp("get_item",
        Description = "Returns a single catalog item by id.",
        Category = "catalog",
        Examples = ["Use after get_catalog to fetch details for a specific id."],
        Hints = ["id must be an integer."])]
    public IActionResult GetById(int id)
    {
        var item = Items.Find(i => i.Id == id);
        if (item is null)
            return NotFound();
        return Ok(item);
    }
}

public record CatalogItem(int Id, string Name);
