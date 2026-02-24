using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SwaggerMcp.Tests;

public sealed class McpEndpointIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public McpEndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = "2024-11-05", clientInfo = new { name = "test", version = "1.0" } }
        });

        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["protocolVersion"]!.GetValue<string>().Should().Be("2024-11-05");
        result["serverInfo"]!.AsObject()["name"]!.GetValue<string>().Should().Be("Orders API");
    }

    [Fact]
    public async Task ToolsList_ReturnsTaggedToolsOnly()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list"
        });

        response.Should().HaveProperty("result");
        var tools = response["result"]!.AsObject()["tools"]!.AsArray();
        var toolNames = tools.Select(t => t!.AsObject()["name"]!.GetValue<string>()).ToList();

        toolNames.Should().Contain("get_order");
        toolNames.Should().Contain("list_orders");
        toolNames.Should().Contain("create_order");
        toolNames.Should().Contain("update_order_status");
        toolNames.Should().NotContain("delete_order");
    }

    [Fact]
    public async Task ToolCall_GetOrder_ReturnsOrder()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { id = 1 } }
        });

        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        var content = result["content"]!.AsArray()[0]!.AsObject()["text"]!.GetValue<string>();
        var order = JsonSerializer.Deserialize<JsonElement>(content);
        order.GetProperty("id").GetInt32().Should().Be(1);
        order.GetProperty("customerName").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task ToolCall_ListOrders_FiltersByStatus()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "tools/call",
            @params = new { name = "list_orders", arguments = new { status = "pending" } }
        });

        var content = ExtractTextContent(response);
        var orders = JsonSerializer.Deserialize<JsonElement[]>(content);
        orders.Should().AllSatisfy(o => o.GetProperty("status").GetString().Should().Be("pending"));
    }

    [Fact]
    public async Task ToolCall_CreateOrder_CreatesAndReturnsOrder()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "tools/call",
            @params = new
            {
                name = "create_order",
                arguments = new { customerName = "Charlie", product = "Thingamajig", quantity = 5 }
            }
        });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        var content = ExtractTextContent(response);
        var order = JsonSerializer.Deserialize<JsonElement>(content);
        order.GetProperty("customerName").GetString().Should().Be("Charlie");
        order.GetProperty("status").GetString().Should().Be("pending");
    }

    [Fact]
    public async Task ToolCall_UnknownTool_ReturnsError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 6,
            method = "tools/call",
            @params = new { name = "nonexistent_tool", arguments = new { } }
        });
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ToolCall_GetOrder_NotFound_ReturnsError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 7,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { id = 9999 } }
        });
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task InvalidJsonRpc_ReturnsError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "1.0",
            id = 8,
            method = "tools/list"
        });
        response.Should().HaveProperty("error");
        response["error"]!.AsObject()["code"]!.GetValue<int>().Should().Be(-32600);
    }

    private async Task<System.Text.Json.Nodes.JsonObject> PostMcpAsync(object body)
    {
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpResponse = await _client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        return System.Text.Json.Nodes.JsonNode.Parse(responseJson)!.AsObject();
    }

    private static string ExtractTextContent(System.Text.Json.Nodes.JsonObject response)
    {
        return response["result"]!.AsObject()["content"]!.AsArray()[0]!
            .AsObject()["text"]!.GetValue<string>();
    }
}
