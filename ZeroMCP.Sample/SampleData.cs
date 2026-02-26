using SampleApi.Controllers;

namespace SampleApi;

/// <summary>
/// Shared in-memory store for sample API (customers, products, orders).
/// Used by Customer, Product, and Orders controllers.
/// </summary>
public static class SampleData
{
    public static readonly List<Customer> Customers = new()
    {
        new Customer { Id = 1, Name = "Alice", Email = "alice@example.com" },
        new Customer { Id = 2, Name = "Bob", Email = "bob@example.com" },
    };

    public static readonly List<Product> Products = new()
    {
        new Product { Id = 1, Name = "Widget", Price = 9.99m },
        new Product { Id = 2, Name = "Gadget", Price = 19.99m },
    };

    public static readonly List<Order> Orders = new()
    {
        new Order { Id = 1, CustomerId = 1, CustomerName = "Alice", Product = "Widget", Quantity = 3, Status = "shipped" },
        new Order { Id = 2, CustomerId = 2, CustomerName = "Bob", Product = "Gadget", Quantity = 1, Status = "pending" },
    };
}
