using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SampleApi;
using ZeroMCP.Attributes;

namespace SampleApi.Controllers;

/// <summary>
/// Demonstrates the three new MCP schema attributes beyond [Mcp]:
///
///   [McpResource]  – exposes a static, well-known URI as an MCP resource
///   [McpTemplate]  – exposes a parameterised URI template as an MCP resource template
///   [McpPrompt]    – exposes a controller action as a reusable MCP prompt
///
/// All three map onto the same ASP.NET Core controller/action pattern, so
/// existing validation, auth, and DI all continue to work as normal.
/// </summary>
[ApiController]
[Route("api/catalog")]
public class CatalogController : ControllerBase
{
    // -------------------------------------------------------------------------
    // Static resources — [McpResource]
    // These are fixed URIs that return a well-known document.
    // MCP clients call resources/list to discover them, then
    // resources/read with the exact URI to fetch the content.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns high-level metadata about the catalog: name, version, product count,
    /// and the list of available categories.
    /// Exposed as the static MCP resource <c>catalog://info</c>.
    /// </summary>
    [HttpGet("info")]
    [McpResource("catalog://info", "catalog_info",
        Description = "High-level metadata about the product catalog: version, total product count, and available categories.",
        MimeType = "application/json")]
    public ActionResult<CatalogInfo> GetCatalogInfo()
        => Ok(SampleData.BuildCatalogInfo());

    /// <summary>
    /// Returns the complete list of product categories.
    /// Exposed as the static MCP resource <c>catalog://categories</c>.
    /// </summary>
    [HttpGet("categories")]
    [McpResource("catalog://categories", "catalog_categories",
        Description = "Complete list of product categories available in the catalog.",
        MimeType = "application/json")]
    public ActionResult<string[]> GetCategories()
        => Ok(SampleData.Categories);

    // -------------------------------------------------------------------------
    // Parameterised resource templates — [McpTemplate]
    // These accept RFC 6570 level-1 URI templates.  The framework extracts
    // variables from the incoming resources/read URI and maps them to action
    // parameters by name, exactly like a route parameter.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a single product by its numeric ID.
    /// Exposed as the MCP resource template <c>catalog://products/{id}</c>.
    /// Example read URI: <c>catalog://products/2</c>
    /// </summary>
    [HttpGet("products/{id:int}")]
    [McpTemplate("catalog://products/{id}", "catalog_product",
        Description = "Retrieves a single product by its numeric ID.",
        MimeType = "application/json")]
    public ActionResult<Product> GetProductById(int id)
    {
        var product = SampleData.Products.FirstOrDefault(p => p.Id == id);
        return product is null
            ? NotFound($"Product {id} not found.")
            : Ok(product);
    }

    /// <summary>
    /// Returns all products that belong to the given category.
    /// Exposed as the MCP resource template <c>catalog://categories/{category}/products</c>.
    /// Example read URI: <c>catalog://categories/Hardware/products</c>
    /// </summary>
    [HttpGet("categories/{category}/products")]
    [McpTemplate("catalog://categories/{category}/products", "catalog_products_by_category",
        Description = "Lists all products in the specified category.",
        MimeType = "application/json")]
    public ActionResult<IEnumerable<Product>> GetProductsByCategory(string category)
    {
        var products = SampleData.Products
            .Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return products.Count == 0
            ? NotFound($"No products found in category '{category}'.")
            : Ok(products);
    }

    // -------------------------------------------------------------------------
    // Prompt templates — [McpPrompt]
    // The action builds and returns a plain-text prompt string.
    // The framework wraps it in the MCP prompt message envelope:
    //   { "messages": [{ "role": "user", "content": { "type": "text", "text": "..." } }] }
    // Arguments are discovered automatically from the action's parameters and
    // surfaced in the prompts/list response so the client knows what to pass.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a ready-to-use search prompt.
    /// The <paramref name="keyword"/> is required; <paramref name="category"/> is optional.
    /// Exposed as the MCP prompt <c>search_products_prompt</c>.
    /// </summary>
    [HttpGet("prompts/search")]
    [McpPrompt("search_products_prompt",
        Description = "Generates a prompt that instructs the model to search for products matching a keyword, with an optional category filter.")]
    public IActionResult SearchProductsPrompt(
        [FromQuery][Required] string keyword,
        [FromQuery] string? category = null)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return BadRequest("keyword is required.");

        var categoryClause = string.IsNullOrWhiteSpace(category)
            ? string.Empty
            : $" within the '{category}' category";

        var prompt = $"""
            Search the product catalog for items matching '{keyword}'{categoryClause}.
            For each result include:
            - Product name and ID
            - Price (formatted as currency)
            - Category
            - A one-sentence description
            Present the results as a concise bullet list, sorted by price ascending.
            """;

        return Ok(prompt);
    }

    /// <summary>
    /// Returns a restock recommendation prompt for a specific product.
    /// Exposed as the MCP prompt <c>restock_recommendation_prompt</c>.
    /// </summary>
    [HttpGet("prompts/restock")]
    [McpPrompt("restock_recommendation_prompt",
        Description = "Generates a restock recommendation prompt for a given product, asking the model to suggest an optimal restock quantity and timeline.")]
    public IActionResult RestockRecommendationPrompt([FromQuery] int productId)
    {
        var product = SampleData.Products.FirstOrDefault(p => p.Id == productId);
        if (product is null)
            return NotFound($"Product {productId} not found.");

        var prompt = $"""
            You are a supply-chain analyst. Review the following product and recommend a restock strategy.

            Product: {product.Name}
            ID: {product.Id}
            Category: {product.Category}
            Unit Price: {product.Price:C2}
            Description: {product.Description ?? "N/A"}

            Based on typical demand patterns for {product.Category} products at this price point:
            1. Recommend an optimal restock quantity.
            2. Suggest the best restock interval (e.g. weekly, bi-weekly).
            3. Identify any seasonality or demand spikes to plan for.
            Justify each recommendation with a brief rationale.
            """;

        return Ok(prompt);
    }
}
