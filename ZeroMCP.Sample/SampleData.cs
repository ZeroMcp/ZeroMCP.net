using SampleApi.Controllers;

namespace SampleApi;

/// <summary>
/// Shared in-memory store for sample API (customers, products, orders).
/// Used by Customer, Product, Orders, and Catalog controllers.
/// </summary>
public static class SampleData
{
    public static readonly List<Customer> Customers =
    [
        new Customer { Id = 1, Name = "Alice", Email = "alice@example.com" },
        new Customer { Id = 2, Name = "Bob",   Email = "bob@example.com"   },
    ];

    public static readonly List<Product> Products =
    [
        new Product { Id = 1, Name = "Widget",       Price = 9.99m,  Category = "Hardware",     Description = "A sturdy general-purpose widget." },
        new Product { Id = 2, Name = "Gadget",       Price = 19.99m, Category = "Electronics",  Description = "A compact multi-function gadget." },
        new Product { Id = 3, Name = "Thingamajig",  Price = 4.49m,  Category = "Hardware",     Description = "Small but essential thingamajig." },
        new Product { Id = 4, Name = "Doohickey",    Price = 14.99m, Category = "Electronics",  Description = "Plug-and-play doohickey adapter." },
        new Product { Id = 5, Name = "Whatchamacallit", Price = 2.99m, Category = "Accessories", Description = "Handy whatchamacallit for everyday use." },
    ];

    public static readonly List<Order> Orders =
    [
        new Order { Id = 1, CustomerId = 1, CustomerName = "Alice", Product = "Widget",  Quantity = 3, Status = "shipped"  },
        new Order { Id = 2, CustomerId = 2, CustomerName = "Bob",   Product = "Gadget",  Quantity = 1, Status = "pending"  },
        new Order { Id = 3, CustomerId = 1, CustomerName = "Alice", Product = "Doohickey", Quantity = 2, Status = "pending" },
    ];

    /// <summary>Returns the distinct categories present in the product catalogue.</summary>
    public static string[] Categories
        => Products.Select(p => p.Category).Distinct().OrderBy(c => c).ToArray();

    /// <summary>Builds a snapshot of catalog metadata for the static <c>catalog://info</c> resource.</summary>
    public static CatalogInfo BuildCatalogInfo()
    {
        var categories = Categories;
        return new CatalogInfo
        {
            Name = "Orders API Product Catalog",
            Version = "1.0",
            ProductCount = Products.Count,
            CategoryCount = categories.Length,
            Categories = categories
        };
    }
}
