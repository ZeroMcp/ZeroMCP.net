using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Mvc.Testing;
using SampleApi.Controllers;

namespace ZeroMCP.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class McpEndpointBenchmarks
{
    private HttpClient _client = null!;
    private StringContent _toolsListContent = null!;
    private StringContent _toolsCallListOrdersContent = null!;
    private StringContent _toolsCallGetOrderContent = null!;

    [GlobalSetup]
    public void Setup()
    {
        var factory = new WebApplicationFactory<OrdersController>();
        _client = factory.CreateClient();
        _toolsListContent = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
            Encoding.UTF8,
            "application/json");
        _toolsCallListOrdersContent = new StringContent(
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_orders","arguments":{}}}""",
            Encoding.UTF8,
            "application/json");
        _toolsCallGetOrderContent = new StringContent(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_order","arguments":{"id":1}}}""",
            Encoding.UTF8,
            "application/json");
    }

    [Benchmark(Description = "GET /mcp/tools (inspector)")]
    public async Task GetToolsInspector()
    {
        using var response = await _client.GetAsync("/mcp/tools");
        response.EnsureSuccessStatusCode();
        await response.Content.ReadAsStringAsync();
    }

    [Benchmark(Description = "POST /mcp tools/list")]
    public async Task PostToolsList()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = _toolsListContent };
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        await response.Content.ReadAsStringAsync();
    }

    [Benchmark(Description = "POST /mcp tools/call list_orders")]
    public async Task PostToolsCallListOrders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = _toolsCallListOrdersContent };
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        await response.Content.ReadAsStringAsync();
    }

    [Benchmark(Description = "POST /mcp tools/call get_order")]
    public async Task PostToolsCallGetOrder()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = _toolsCallGetOrderContent };
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        await response.Content.ReadAsStringAsync();
    }
}
