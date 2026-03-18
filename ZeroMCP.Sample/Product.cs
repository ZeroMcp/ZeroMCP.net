namespace SampleApi;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
}

/// <summary>High-level metadata about the product catalog, returned as a static MCP resource.</summary>
public sealed class CatalogInfo
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public int ProductCount { get; init; }
    public int CategoryCount { get; init; }
    public string[] Categories { get; init; } = [];
}
