using SampleApi.Controllers;

namespace SampleApi
{
    public class Repository
    {
        public List<Order> Orders { get; } = new()
        {
            new Order { Id = 1, CustomerId = 1, CustomerName = "Alice", Product = "Widget", Quantity = 3, Status = "shipped" },
            new Order { Id = 2, CustomerId = 2, CustomerName = "Bob", Product = "Gadget", Quantity = 1, Status = "pending" },
        };
        public List<Customer> Customers { get; } = new()
        {
            new Customer { Id = 1, Name = "Alice Smith", Email = "testCustomer" },
            new Customer { Id = 2, Name = "Bob Johnson", Email = "testCustomer" }
        };
        public List<Product> Products { get; } = new()
        {
            new Product { Id = 1, Name = "Widget", Price = 9.99m },
            new Product { Id = 2, Name = "Gadget", Price = 19.99m }
        };
    }
}
