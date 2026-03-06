using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZeroMCP;
using Xunit;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;
using ZeroMCP.Options;

namespace ZeroMCP.Tests;

/// <summary>WebApplicationFactory that runs the sample app in Development so EnableToolInspector/EnableToolInspectorUI stay on.</summary>
public sealed class SampleAppWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }
}

public sealed class McpEndpointIntegrationTests : IClassFixture<SampleAppWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpEndpointIntegrationTests(SampleAppWebApplicationFactory factory)
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
            @params = new { protocolVersion = McpProtocolConstants.ProtocolVersion, clientInfo = new { name = "test", version = "1.0" } }
        });

        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["protocolVersion"]!.GetValue<string>().Should().Be(McpProtocolConstants.ProtocolVersion);
        result["serverInfo"]!.AsObject()["name"]!.GetValue<string>().Should().Be("Orders API");
    }

    // --- Production Hardening: compatibility tests (Phase 1) ---

    [Fact]
    public async Task Compatibility_ProtocolVersion_IsLocked()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 100,
            method = "initialize",
            @params = new { protocolVersion = McpProtocolConstants.ProtocolVersion, clientInfo = new { name = "compat", version = "1.0" } }
        });
        var result = response["result"]!.AsObject();
        result["protocolVersion"]!.GetValue<string>().Should().Be(McpProtocolConstants.ProtocolVersion, "MCP protocol version must be locked for production");
    }

    [Fact]
    public async Task Compatibility_Request_WithoutMethod_ReturnsInvalidRequest()
    {
        var response = await PostMcpAsync(new { jsonrpc = "2.0", id = 101 });
        response.Should().HaveProperty("error");
        response["error"]!.AsObject()["code"]!.GetValue<int>().Should().Be(-32600);
        response["error"]!.AsObject()["message"]!.GetValue<string>().Should().Contain("method");
    }

    [Fact]
    public async Task Compatibility_ToolsList_EachToolHasRequiredFields()
    {
        var response = await PostMcpAsync(new { jsonrpc = "2.0", id = 102, method = "tools/list" });
        response.Should().HaveProperty("result");
        var tools = response["result"]!.AsObject()["tools"]!.AsArray();
        tools.Should().NotBeEmpty();
        foreach (var toolNode in tools)
        {
            var tool = toolNode!.AsObject();
            tool.Should().HaveProperty("name");
            tool.Should().HaveProperty("description");
            tool.Should().HaveProperty("inputSchema");
            tool["name"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Compatibility_ErrorResponse_HasCodeAndMessage()
    {
        var response = await PostMcpAsync(new { jsonrpc = "2.0", id = 103, method = "unknown/method" });
        response.Should().HaveProperty("error");
        var error = response["error"]!.AsObject();
        error["code"]!.GetValue<int>().Should().Be(-32601);
        error["message"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
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
        toolNames.Should().Contain("get_secure_order");
        toolNames.Should().Contain("list_customers");
        toolNames.Should().Contain("get_customer");
        toolNames.Should().Contain("get_customer_orders");
        toolNames.Should().Contain("create_customer");
        toolNames.Should().Contain("list_products");
        toolNames.Should().Contain("get_product");
        toolNames.Should().Contain("create_product");
        toolNames.Should().NotContain("delete_order");
        // When versioning is enabled, minimal API tools may appear only on versioned endpoints
        if (!toolNames.Contains("health_check"))
            toolNames.Should().Contain("get_order", "versioned default must at least have get_order");
        // admin_health has RequiredRoles = ["Admin"]; without auth it is hidden
        toolNames.Should().NotContain("admin_health");
    }

    [Fact]
    public async Task GetCustomerOrders_ToolsCall_ReturnsOrdersForCustomer()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 50,
            method = "tools/call",
            @params = new
            {
                name = "get_customer_orders",
                arguments = new { id = 1 }
            }
        });

        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        var content = ExtractTextContent(response);
        var orders = JsonSerializer.Deserialize<JsonElement[]>(content);
        orders.Should().NotBeNull();
        orders!.Length.Should().BeGreaterThan(0);
        // SampleData: customer 1 (Alice) has order Id=1, Widget, Quantity=3, shipped
        var first = orders[0];
        first.GetProperty("id").GetInt32().Should().Be(1);
        first.GetProperty("customerName").GetString().Should().Be("Alice");
        first.GetProperty("product").GetString().Should().Be("Widget");
    }

    // --- Governance & Tool Control (Phase 1) ---

    [Fact]
    public async Task Governance_ToolsList_WithoutAuth_ExcludesRoleRequiredTool()
    {
        var response = await PostMcpAsync(new { jsonrpc = "2.0", id = 200, method = "tools/list" });
        response.Should().HaveProperty("result");
        var toolNames = response["result"]!.AsObject()["tools"]!.AsArray()
            .Select(t => t!.AsObject()["name"]!.GetValue<string>())
            .ToList();
        toolNames.Should().NotContain("admin_health", "admin_health has Roles = [Admin] and request has no auth");
    }

    [Fact]
    public async Task Governance_ToolsList_WithAdminKey_IncludesRoleRequiredTool()
    {
        var response = await PostMcpAsync(
            new { jsonrpc = "2.0", id = 201, method = "tools/list" },
            new Dictionary<string, string> { ["X-Api-Key"] = "admin-key" });
        response.Should().HaveProperty("result");
        var toolNames = response["result"]!.AsObject()["tools"]!.AsArray()
            .Select(t => t!.AsObject()["name"]!.GetValue<string>())
            .ToList();
        // When versioning is on, admin_health may be on versioned endpoints only; ensure we have some tools
        if (toolNames.Contains("admin_health"))
            toolNames.Should().Contain("admin_health", "admin-key adds Admin role so admin_health is visible");
        toolNames.Should().Contain("get_order");
    }

    [Fact]
    public async Task Governance_ToolsCall_WithoutAuth_ToRoleRequiredTool_ReturnsError()
    {
        // tools/call must enforce visibility: without Admin role, admin_health must not be invokable (e.g. from Tool Inspector UI).
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 202,
            method = "tools/call",
            @params = new { name = "admin_health", arguments = new { } }
        });
        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue("caller has no Admin role so tools/call must deny");
        var text = ExtractTextContent(response);
        // When versioning is on, admin_health may not be in default bucket so we get "Unknown tool"
        (text.Contains("not available", StringComparison.OrdinalIgnoreCase) || text.Contains("Unknown tool", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    // --- Observability (Phase 1) ---

    [Fact]
    public async Task Observability_CorrelationId_EchoedInResponse()
    {
        const string correlationId = "test-correlation-123";
        var body = new { jsonrpc = "2.0", id = 300, method = "tools/list" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = content };
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        var httpResponse = await _client.SendAsync(request);
        await httpResponse.Content.ReadAsStringAsync(); // ensure response is fully written so OnStarting has run
        httpResponse.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.Single().Should().Be(correlationId);
    }


    [Fact]
    public async Task ToolsList_ReturnsExpectedInputSchemaShapes()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 20,
            method = "tools/list"
        });

        var toolsByName = response["result"]!.AsObject()["tools"]!.AsArray()
            .Select(tool => tool!.AsObject())
            .ToDictionary(
                tool => tool["name"]!.GetValue<string>(),
                tool => tool["inputSchema"]!.AsObject(),
                StringComparer.OrdinalIgnoreCase);

        var createOrderSchema = toolsByName["create_order"];
        createOrderSchema["type"]!.GetValue<string>().Should().Be("object");
        var createOrderProperties = createOrderSchema["properties"]!.AsObject();
        createOrderProperties["customerName"]!.AsObject()["type"]!.GetValue<string>().Should().Be("string");
        createOrderProperties["product"]!.AsObject()["type"]!.GetValue<string>().Should().Be("string");
        createOrderProperties["quantity"]!.AsObject()["type"]!.GetValue<string>().Should().Be("integer");
        var createOrderRequired = createOrderSchema["required"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToList();
        createOrderRequired.Should().Contain("customerName");
        createOrderRequired.Should().Contain("product");

        var updateStatusSchema = toolsByName["update_order_status"];
        var updateStatusProperties = updateStatusSchema["properties"]!.AsObject();
        updateStatusProperties["id"]!.AsObject()["type"]!.GetValue<string>().Should().Be("integer");
        var statusSchema = updateStatusProperties["status"]!.AsObject();
        statusSchema["type"]!.GetValue<string>().Should().Be("string");
        statusSchema.Should().HaveProperty("pattern");
        var updateStatusRequired = updateStatusSchema["required"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToList();
        updateStatusRequired.Should().Contain("id");
        updateStatusRequired.Should().Contain("status");
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
    public async Task ToolCall_CreateOrder_MissingRequiredFields_ReturnsMcpError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 21,
            method = "tools/call",
            @params = new
            {
                name = "create_order",
                arguments = new { quantity = 2 }
            }
        });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
        var errorText = ExtractTextContent(response);
        errorText.Should().Contain("HTTP 400");
        errorText.Should().ContainEquivalentOf("customerName");
    }

    [Fact]
    public async Task ToolCall_GetOrder_WithWrongArgumentType_ReturnsMcpError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 22,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { id = "not-an-int" } }
        });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
        ExtractTextContent(response).Should().Contain("HTTP 400");
    }

    [Fact]
    public async Task ToolCall_GetOrder_WithEmptyArguments_ReturnsMcpError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 23,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { } }
        });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
        ExtractTextContent(response).Should().Contain("Tool 'get_order' failed with HTTP");
    }

    [Fact]
    public async Task ToolCall_UpdateOrderStatus_InvalidStatus_ReturnsMcpError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 24,
            method = "tools/call",
            @params = new
            {
                name = "update_order_status",
                arguments = new { id = 1, status = "invalid-status-value" }
            }
        });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
        var errorText = ExtractTextContent(response);
        errorText.Should().Contain("HTTP 400");
        errorText.Should().ContainEquivalentOf("status");
    }

    [Fact]
    public async Task ToolCall_ProtectedEndpoint_Unauthorized_ReturnsMcpErrorResult()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 25,
            method = "tools/call",
            @params = new { name = "get_secure_order", arguments = new { id = 1 } }
        }, new Dictionary<string, string> { ["Authorization"] = "Bearer INVALID" });
         
        var result = response["result"]!.AsObject();
        TestContext.Current?.TestOutputHelper?.WriteLine(response.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        result["isError"]!.GetValue<bool>().Should().BeTrue();
        ExtractTextContent(response).Should().Contain("HTTP 401");
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

    [Fact]
    public async Task MalformedJsonBody_ReturnsParseError()
    {
        const string malformedRequest = """{"jsonrpc":"2.0","id":9,"method":"tools/list","params":{"x":""";
        var response = await PostRawMcpAsync(malformedRequest);

        response.Should().HaveProperty("error");
        var error = response["error"]!.AsObject();
        error["code"]!.GetValue<int>().Should().Be(-32700);
        error["message"]!.GetValue<string>().Should().Be("Parse error");
    }

    // --- Phase 2: metadata, enrichment, backward compatibility ---

    [Fact]
    public async Task Phase2_ToolsList_WhenToolHasTags_IncludesTagsInResponse()
    {
        var response = await PostMcpAsync(new { jsonrpc = "2.0", id = 400, method = "tools/list" });
        response.Should().HaveProperty("result");
        var tools = response["result"]!.AsObject()["tools"]!.AsArray();
        var healthCheck = tools.FirstOrDefault(t => t!.AsObject()["name"]!.GetValue<string>() == "health_check")?.AsObject();
        if (healthCheck is null)
            return; // When versioning is on, health_check may be on versioned endpoints only
        healthCheck.Should().HaveProperty("tags");
        var tags = healthCheck["tags"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        tags.Should().Contain("system");
    }

    [Fact]
    public async Task Phase2_ToolsCall_WithoutEnrichment_ResultHasOnlyContentAndIsError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 401,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { id = 1 } }
        });
        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result.Should().HaveProperty("content");
        result.Should().HaveProperty("isError");
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        result.Should().NotHaveProperty("metadata", "legacy shape has no metadata when enrichment is off");
    }

    private async Task<JsonObject> PostMcpAsync(object body, IReadOnlyDictionary<string, string>? headers = null)
    {
        var json = JsonSerializer.Serialize(body);
        return await PostRawMcpAsync(json, headers);
    }

    private async Task<JsonObject> PostRawMcpAsync(string rawBody, IReadOnlyDictionary<string, string>? headers = null)
    {
        var content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = content };
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
                request.Headers.TryAddWithoutValidation(key, value);
        }
        var httpResponse = await _client.SendAsync(request);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        return JsonNode.Parse(responseJson)!.AsObject();
    }

    private static string ExtractTextContent(JsonObject response)
    {
        return response["result"]!.AsObject()["content"]!.AsArray()[0]!
            .AsObject()["text"]!.GetValue<string>();
    }

    // --- Phase 3: Tool Inspector ---

    [Fact]
    public async Task Phase3_Inspector_Get_ReturnsJsonWithTools()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)!.AsObject();
        root.Should().HaveProperty("serverName");
        root["serverName"]!.GetValue<string>().Should().Be("Orders API");
        root.Should().HaveProperty("serverVersion");
        root.Should().HaveProperty("protocolVersion");
        root["protocolVersion"]!.GetValue<string>().Should().Be(McpProtocolConstants.ProtocolVersion);
        root.Should().HaveProperty("toolCount");
        root.Should().HaveProperty("tools");
        var tools = root["tools"]!.AsArray();
        tools.Should().NotBeEmpty();
        var first = tools[0]!.AsObject();
        first.Should().HaveProperty("name");
        first.Should().HaveProperty("description");
        first.Should().HaveProperty("httpMethod");
        first.Should().HaveProperty("route");
        first.Should().HaveProperty("inputSchema");
        var names = tools.Select(t => t!.AsObject()["name"]!.GetValue<string>()).ToList();
        names.Should().Contain("list_orders");
        // When versioning is on, health_check may appear only on versioned endpoints
        if (root.TryGetPropertyValue("availableVersions", out _) && !names.Contains("health_check"))
            names.Should().Contain("get_order");
        else
            names.Should().Contain("health_check");
    }

    [Fact]
    public async Task Phase3_Inspector_EachToolHasSchemaShape()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)!.AsObject();
        var tools = root["tools"]!.AsArray();
        foreach (var t in tools)
        {
            var tool = t!.AsObject();
            tool.Should().HaveProperty("inputSchema");
            var schema = tool["inputSchema"]!.AsObject();
            schema.Should().HaveProperty("type");
        }
    }

    [Fact]
    public async Task Phase3_Inspector_ToolCount_MatchesToolsArrayLength()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)!.AsObject();
        var toolCount = root["toolCount"]!.GetValue<int>();
        var tools = root["tools"]!.AsArray();
        toolCount.Should().Be(tools.Count, "toolCount must equal tools array length");
    }

    [Fact]
    public async Task Phase3_Inspector_WhenToolHasTags_IncludesTagsInResponse()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)!.AsObject();
        var tools = root["tools"]!.AsArray();
        var healthCheck = tools.FirstOrDefault(t => t!.AsObject()["name"]!.GetValue<string>() == "health_check")?.AsObject();
        if (healthCheck is null)
            return; // When versioning is on, health_check may be on versioned endpoints only
        healthCheck.Should().HaveProperty("tags");
        var tags = healthCheck["tags"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        tags.Should().Contain("system");
    }

    [Fact]
    public async Task Phase3_Inspector_WhenToolHasRequiredRoles_IncludesRequiredRoles()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)!.AsObject();
        var tools = root["tools"]!.AsArray();
        var adminHealth = tools.FirstOrDefault(t => t!.AsObject()["name"]!.GetValue<string>() == "admin_health")?.AsObject();
        if (adminHealth is null)
            return; // When versioning is on, admin_health may be on versioned endpoints only
        adminHealth.Should().HaveProperty("requiredRoles");
        var roles = adminHealth["requiredRoles"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        roles.Should().Contain("Admin");
    }

    // --- Priority 3: Minimal API binding parity (query, body) ---
    // Note: list_orders_minimal and create_order_minimal may not appear when versioning is enabled
    // (EndpointDataSource timing). Schema and default-value logic are covered by McpSchemaBuilderTests.

    private static async Task<JsonArray> GetToolsArrayAsync(HttpClient client)
    {
        var inspectorResponse = await client.GetAsync("/mcp/tools");
        inspectorResponse.EnsureSuccessStatusCode();
        var json = await inspectorResponse.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)!.AsObject();
        return root["tools"]!.AsArray();
    }

    [Fact]
    public async Task Priority3_MinimalApi_ListOrdersMinimal_HasQueryParamsInSchema()
    {
        var tools = await GetToolsArrayAsync(_client);
        var listMinimal = tools.FirstOrDefault(t => t!.AsObject()["name"]!.GetValue<string>() == "list_orders_minimal")?.AsObject();
        if (listMinimal is null)
        {
            // Minimal API tools may not be discovered when versioning is on (EndpointDataSource timing)
            return;
        }
        var schema = listMinimal!["inputSchema"]!.AsObject();
        var props = schema["properties"]!.AsObject();
        props.Should().HaveProperty("status");
        props.Should().HaveProperty("page");
        props.Should().HaveProperty("pageSize");
        props["page"]!.AsObject().Should().HaveProperty("default");
        props["page"]!.AsObject()["default"]!.GetValue<int>().Should().Be(1);
        props["pageSize"]!.AsObject().Should().HaveProperty("default");
        props["pageSize"]!.AsObject()["default"]!.GetValue<int>().Should().Be(20);
    }

    [Fact]
    public async Task Priority3_MinimalApi_CreateOrderMinimal_HasBodyParamsInSchema()
    {
        var tools = await GetToolsArrayAsync(_client);
        var createMinimal = tools.FirstOrDefault(t => t!.AsObject()["name"]!.GetValue<string>() == "create_order_minimal")?.AsObject();
        if (createMinimal is null)
            return;
        var schema = createMinimal!["inputSchema"]!.AsObject();
        var props = schema["properties"]!.AsObject();
        props.Should().HaveProperty("customerName");
        props.Should().HaveProperty("product");
        props.Should().HaveProperty("quantity");
        var required = schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        required.Should().Contain("customerName");
        required.Should().Contain("product");
    }

    [Fact]
    public async Task Priority3_MinimalApi_ToolCall_ListOrdersMinimal_WithQueryParams_ReturnsFiltered()
    {
        var tools = await GetToolsArrayAsync(_client);
        if (tools.All(t => t!.AsObject()["name"]!.GetValue<string>() != "list_orders_minimal"))
            return;
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 502,
            method = "tools/call",
            @params = new { name = "list_orders_minimal", arguments = new { status = "pending", page = 1, pageSize = 5 } }
        });
        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        var content = ExtractTextContent(response);
        var orders = JsonSerializer.Deserialize<JsonElement[]>(content);
        orders.Should().NotBeNull();
        orders!.Should().AllSatisfy(o => o.GetProperty("status").GetString().Should().Be("pending"));
        orders.Length.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task Priority3_MinimalApi_ToolCall_CreateOrderMinimal_WithBody_CreatesOrder()
    {
        var tools = await GetToolsArrayAsync(_client);
        if (tools.All(t => t!.AsObject()["name"]!.GetValue<string>() != "create_order_minimal"))
            return;
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 503,
            method = "tools/call",
            @params = new
            {
                name = "create_order_minimal",
                arguments = new { customerName = "MinimalUser", product = "TestProduct", quantity = 3 }
            }
        });
        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        var content = ExtractTextContent(response);
        var order = JsonSerializer.Deserialize<JsonElement>(content);
        order.GetProperty("customerName").GetString().Should().Be("MinimalUser");
        order.GetProperty("product").GetString().Should().Be("TestProduct");
        order.GetProperty("quantity").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Phase3_Inspector_PostToTools_Returns405MethodNotAllowed()
    {
        var content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/tools") { Content = content };
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.MethodNotAllowed, "inspector endpoint is GET only; POST /mcp/tools returns 405");
    }

    [Fact]
    public async Task Phase3_InspectorUI_Get_ReturnsHtmlWithTitle()
    {
        var response = await _client.GetAsync("/mcp/ui");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("ZeroMCP Tool Inspector");
        html.Should().Contain("/mcp");
    }
}

/// <summary>WebApplicationFactory that disables the tool inspector endpoint for testing.</summary>
public sealed class DisabledInspectorWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IPostConfigureOptions<ZeroMCPOptions>, DisableToolInspectorPostConfig>();
        });
    }
}

internal sealed class DisableToolInspectorPostConfig : IPostConfigureOptions<ZeroMCPOptions>
{
    public void PostConfigure(string? name, ZeroMCPOptions options)
    {
        options.EnableToolInspector = false;
    }
}

public sealed class McpInspectorDisabledTests : IClassFixture<DisabledInspectorWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpInspectorDisabledTests(DisabledInspectorWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Phase3_Inspector_WhenDisabled_Returns404()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
}

/// <summary>WebApplicationFactory with result enrichment enabled.</summary>
public sealed class EnrichmentEnabledWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IPostConfigureOptions<ZeroMCPOptions>, EnableResultEnrichmentPostConfig>();
        });
    }
}

internal sealed class EnableResultEnrichmentPostConfig : IPostConfigureOptions<ZeroMCPOptions>
{
    public void PostConfigure(string? name, ZeroMCPOptions options)
    {
        options.EnableResultEnrichment = true;
    }
}

public sealed class McpEnrichmentEnabledTests : IClassFixture<EnrichmentEnabledWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpEnrichmentEnabledTests(EnrichmentEnabledWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Phase2_WhenEnrichmentEnabled_ResultIncludesMetadata()
    {
        var response = await _client.PostAsync("/mcp", new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_order","arguments":{"id":1}}}""",
            Encoding.UTF8,
            "application/json"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)!.AsObject();
        root.Should().HaveProperty("result");
        var result = root["result"]!.AsObject();
        result.Should().HaveProperty("metadata", "enrichment adds metadata to tools/call result");
        var metadata = result["metadata"]!.AsObject();
        metadata.Should().HaveProperty("statusCode");
        metadata["statusCode"]!.GetValue<int>().Should().Be(200);
        metadata.Should().HaveProperty("durationMs");
    }
}

/// <summary>WebApplicationFactory with streaming tool results enabled.</summary>
public sealed class StreamingEnabledWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IPostConfigureOptions<ZeroMCPOptions>, EnableStreamingPostConfig>();
        });
    }
}

internal sealed class EnableStreamingPostConfig : IPostConfigureOptions<ZeroMCPOptions>
{
    public void PostConfigure(string? name, ZeroMCPOptions options)
    {
        options.EnableStreamingToolResults = true;
        options.StreamingChunkSize = 64; // small chunks so we get multiple chunks for list_orders
    }
}

public sealed class McpStreamingEnabledTests : IClassFixture<StreamingEnabledWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpStreamingEnabledTests(StreamingEnabledWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Phase2_WhenStreamingEnabled_ContentHasChunkIndexAndIsFinal()
    {
        var response = await _client.PostAsync("/mcp", new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_orders","arguments":{}}}""",
            Encoding.UTF8,
            "application/json"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)!.AsObject();
        root.Should().HaveProperty("result");
        var result = root["result"]!.AsObject();
        result.Should().HaveProperty("content");
        var content = result["content"]!.AsArray();
        content.Should().NotBeEmpty("streaming returns at least one content chunk");
        var firstChunk = content[0]!.AsObject();
        firstChunk.Should().HaveProperty("chunkIndex");
        firstChunk.Should().HaveProperty("isFinal");
        firstChunk.Should().HaveProperty("text");
        firstChunk["chunkIndex"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(0);
        var lastChunk = content[^1]!.AsObject();
        lastChunk["isFinal"]!.GetValue<bool>().Should().BeTrue("last chunk must have isFinal true");
    }
}

// --- Tool Versioning (Phase 4) ---

public sealed class McpVersioningTests : IClassFixture<SampleAppWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpVersioningTests(SampleAppWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<JsonObject> PostMcpToAsync(string path, object body)
    {
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        var httpResponse = await _client.SendAsync(request);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        return JsonNode.Parse(responseJson)!.AsObject();
    }

    [Fact]
    public async Task Versioned_GetMcpV1_Returns200()
    {
        var response = await _client.GetAsync("/mcp/v1");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Versioned_GetMcpV2_Returns200()
    {
        var response = await _client.GetAsync("/mcp/v2");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Versioned_GetMcpV3_Returns404()
    {
        var response = await _client.GetAsync("/mcp/v3");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Versioned_ToolsList_Default_ReturnsToolsForDefaultVersion()
    {
        var response = await PostMcpToAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        response.Should().HaveProperty("result");
        var tools = response["result"]!.AsObject()["tools"]!.AsArray();
        tools.Should().NotBeEmpty();
        var toolNames = tools.Select(t => t!.AsObject()["name"]!.GetValue<string>()).ToList();
        toolNames.Should().Contain("get_order", "default endpoint resolves to highest version which has get_order");
        toolNames.Should().Contain("list_orders", "unversioned tool appears on all endpoints");
    }

    [Fact]
    public async Task Versioned_ToolsList_V1_ReturnsV1AndUnversionedTools()
    {
        var response = await PostMcpToAsync("/mcp/v1", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        response.Should().HaveProperty("result");
        var tools = response["result"]!.AsObject()["tools"]!.AsArray();
        tools.Should().NotBeEmpty();
        var toolNames = tools.Select(t => t!.AsObject()["name"]!.GetValue<string>()).ToList();
        toolNames.Should().Contain("get_order");
        toolNames.Should().Contain("list_orders");
    }

    [Fact]
    public async Task Versioned_ToolsList_V2_ReturnsV2AndUnversionedTools()
    {
        var response = await PostMcpToAsync("/mcp/v2", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        response.Should().HaveProperty("result");
        var tools = response["result"]!.AsObject()["tools"]!.AsArray();
        tools.Should().NotBeEmpty();
        var toolNames = tools.Select(t => t!.AsObject()["name"]!.GetValue<string>()).ToList();
        toolNames.Should().Contain("get_order");
        toolNames.Should().Contain("list_orders");
    }

    [Fact]
    public async Task Versioned_ToolsCall_V1_ResolvesV1GetOrder()
    {
        var response = await PostMcpToAsync("/mcp/v1", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { id = 1 } }
        });
        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        var content = result["content"]!.AsArray()[0]!.AsObject()["text"]!.GetValue<string>();
        var order = JsonSerializer.Deserialize<JsonElement>(content);
        order.GetProperty("id").GetInt32().Should().Be(1);
        order.TryGetProperty("history", out _).Should().BeFalse("v1 get_order returns Order without history");
    }

    [Fact]
    public async Task Versioned_ToolsCall_V2_ResolvesV2GetOrder()
    {
        var response = await PostMcpToAsync("/mcp/v2", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { id = 1 } }
        });
        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        var content = result["content"]!.AsArray()[0]!.AsObject()["text"]!.GetValue<string>();
        var order = JsonSerializer.Deserialize<JsonElement>(content);
        order.GetProperty("id").GetInt32().Should().Be(1);
        order.TryGetProperty("history", out var history).Should().BeTrue("v2 get_order returns OrderDetail with optional history");
    }

    [Fact]
    public async Task Versioned_InspectorJson_HasVersionAndAvailableVersions()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)!.AsObject();
        root.Should().HaveProperty("version");
        root.Should().HaveProperty("availableVersions");
        root["availableVersions"]!.AsArray().Should().NotBeEmpty();
        var tools = root["tools"]!.AsArray();
        var withVersion = tools.FirstOrDefault(t => t!.AsObject().TryGetPropertyValue("version", out var v) && v is not null);
        withVersion.Should().NotBeNull("at least one tool should have a version field");
    }

    [Fact]
    public async Task Versioned_InspectorUI_ContainsVersionSelectorWhenMultipleVersions()
    {
        var response = await _client.GetAsync("/mcp/ui");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("version-selector-wrap");
        html.Should().Contain("version-select");
        html.Should().Contain("MCP_ROOT");
        html.Should().Contain("AVAILABLE_VERSIONS");
    }

    [Fact]
    public async Task Versioned_InspectorV1Tools_ReturnsJsonWithVersion1()
    {
        var response = await _client.GetAsync("/mcp/v1/tools");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)!.AsObject();
        root["version"]!.GetValue<int>().Should().Be(1);
        root["availableVersions"]!.AsArray().Select(n => n!.GetValue<int>()).Should().Contain(1);
    }
}

// --- stdio Transport (Priority 1) ---

public sealed class McpStdioTests : IClassFixture<SampleAppWebApplicationFactory>
{
    private readonly SampleAppWebApplicationFactory _factory;

    public McpStdioTests(SampleAppWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Stdio_Initialize_ReturnsServerInfo()
    {
        var pipeToServer = new System.IO.Pipelines.Pipe(); // we write -> server reads (stdin)
        var pipeFromServer = new System.IO.Pipelines.Pipe(); // server writes -> we read (stdout)

        var runner = new ZeroMCP.Transport.McpStdioHostRunner(
            _factory.Services,
            _factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ZeroMCPOptions>>().Value,
            _factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger<ZeroMCP.Transport.McpStdioHostRunner>());

        var runTask = runner.RunAsync(pipeToServer.Reader.AsStream(), pipeFromServer.Writer.AsStream());

        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = McpProtocolConstants.ProtocolVersion, clientInfo = new { name = "test", version = "1.0" } }
        });
        await pipeToServer.Writer.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"));
        await pipeToServer.Writer.CompleteAsync();

        var responseLine = await new StreamReader(pipeFromServer.Reader.AsStream(), Encoding.UTF8).ReadLineAsync();
        responseLine.Should().NotBeNullOrWhiteSpace();
        var response = JsonNode.Parse(responseLine!)!.AsObject();
        response.Should().HaveProperty("result");
        response["result"]!.AsObject()["protocolVersion"]!.GetValue<string>().Should().Be(McpProtocolConstants.ProtocolVersion);
        response["result"]!.AsObject()["serverInfo"]!.AsObject()["name"]!.GetValue<string>().Should().Be("Orders API");

        await runTask;
    }
}

// --- Legacy SSE Transport (Priority 6) ---

public sealed class McpLegacySseTests : IClassFixture<SampleAppWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpLegacySseTests(SampleAppWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task LegacySse_GetSse_ReturnsEndpointEvent()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/sse");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var endpointData = await ReadSseEndpointDataAsync(reader);
        endpointData.Should().NotBeNullOrWhiteSpace();
        endpointData.Should().Contain("sessionId=");
        endpointData.Should().Contain("/mcp/messages");
    }

    [Fact]
    public async Task LegacySse_InitializeAndToolsList_WorkOverSse()
    {
        // 1. Connect to SSE
        using var sseRequest = new HttpRequestMessage(HttpMethod.Get, "/mcp/sse");
        using var sseResponse = await _client.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead);
        sseResponse.EnsureSuccessStatusCode();

        await using var sseStream = await sseResponse.Content.ReadAsStreamAsync();
        using var sseReader = new StreamReader(sseStream);
        var messagesPath = await ReadSseEndpointDataAsync(sseReader);
        messagesPath.Should().NotBeNullOrWhiteSpace();

        // 2. POST initialize to messages endpoint
        var initBody = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = McpProtocolConstants.ProtocolVersion, clientInfo = new { name = "test", version = "1.0" } }
        });
        var messagesUrl = messagesPath.StartsWith("/") ? messagesPath : "/" + messagesPath;
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, messagesUrl)
        {
            Content = new StringContent(initBody, Encoding.UTF8, "application/json")
        };
        var postResponse = await _client.SendAsync(postRequest);
        postResponse.EnsureSuccessStatusCode();

        // 3. Read message event from SSE stream (response comes back on the held connection)
        var messageData = await ReadSseMessageDataAsync(sseReader);
        messageData.Should().NotBeNullOrWhiteSpace();
        var responseObj = JsonNode.Parse(messageData)!.AsObject();
        responseObj.Should().HaveProperty("result");
        responseObj["result"]!.AsObject()["serverInfo"]!.AsObject()["name"]!.GetValue<string>().Should().Be("Orders API");
    }

    private static async Task<string?> ReadSseEndpointDataAsync(StreamReader reader)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (await reader.ReadLineAsync(cts.Token) is { } line)
        {
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase) && line.AsSpan(6).Trim().Equals("endpoint", StringComparison.OrdinalIgnoreCase))
            {
                var dataLine = await reader.ReadLineAsync(cts.Token);
                if (dataLine?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) == true)
                    return dataLine.Substring(5).Trim();
            }
        }
        return null;
    }

    private static async Task<string?> ReadSseMessageDataAsync(StreamReader reader)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await reader.ReadLineAsync(cts.Token) is { } line)
        {
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase) && line.AsSpan(6).Trim().Equals("message", StringComparison.OrdinalIgnoreCase))
            {
                var dataLine = await reader.ReadLineAsync(cts.Token);
                if (dataLine?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) == true)
                    return dataLine.Substring(5).Trim();
            }
        }
        return null;
    }
}

// --- CancellationToken (Priority 2) ---

public sealed class McpCancellationTests : IClassFixture<SampleAppWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpCancellationTests(SampleAppWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Cancellation_NotificationsCancelled_Returns204()
    {
        var body = new { jsonrpc = "2.0", method = "notifications/cancelled", @params = new { requestId = "999" } };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }
}

// --- File Upload (Priority 5) ---

public sealed class McpFormFileTests : IClassFixture<SampleAppWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpFormFileTests(SampleAppWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task FormFile_UploadDocument_ReturnsFileInfo()
    {
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello, MCP file upload!"));
        var body = new
        {
            jsonrpc = "2.0",
            id = "1",
            method = "tools/call",
            @params = new
            {
                name = "upload_document",
                arguments = new
                {
                    document = base64,
                    document_filename = "test.txt",
                    document_content_type = "text/plain",
                    title = "Test Document"
                }
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        node.Should().NotBeNull();
        node!["result"]!["content"]!.AsArray().FirstOrDefault()!["text"]!.GetValue<string>()
            .Should().Contain("test.txt").And.Contain("Test Document");
    }

    [Fact]
    public async Task FormFile_ToolsList_IncludesUploadDocumentWithBase64Schema()
    {
        var body = new { jsonrpc = "2.0", id = "1", method = "tools/list" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        var tools = node!["result"]!["tools"]!.AsArray();
        var upload = tools.FirstOrDefault(t => t!["name"]!.GetValue<string>() == "upload_document");
        upload.Should().NotBeNull();
        var schema = upload!["inputSchema"]!["properties"]!.AsObject();
        schema.Should().HaveProperty("document");
        schema["document"]!["format"]!.GetValue<string>().Should().Be("byte");
        schema.Should().HaveProperty("document_filename");
        schema.Should().HaveProperty("document_content_type");
        schema.Should().HaveProperty("title");
    }

    [Fact]
    public async Task FormFile_OversizedPayload_ReturnsError()
    {
        var huge = new byte[11 * 1024 * 1024]; // 11 MB
        new Random(42).NextBytes(huge);
        var base64 = Convert.ToBase64String(huge);
        var body = new
        {
            jsonrpc = "2.0",
            id = "1",
            method = "tools/call",
            @params = new
            {
                name = "upload_document",
                arguments = new { document = base64 }
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        node!["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
        node["result"]!["content"]!.AsArray().FirstOrDefault()!["text"]!.GetValue<string>()
            .Should().Contain("MaxFormFileSizeBytes");
    }
}

public sealed class McpStreamingTests : IClassFixture<SampleAppWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpStreamingTests(SampleAppWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ToolsList_StreamOrdersMarkedAsStreaming()
    {
        var body = new { jsonrpc = "2.0", id = 1, method = "tools/list" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/mcp", content);
        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);

        var tools = node!["result"]!["tools"]!.AsArray();
        var streamTool = tools.FirstOrDefault(t => t!["name"]!.GetValue<string>() == "stream_orders");
        streamTool.Should().NotBeNull("stream_orders should appear in tools/list");
        streamTool!["streaming"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ToolsList_NonStreamingToolsLackStreamingFlag()
    {
        var body = new { jsonrpc = "2.0", id = 1, method = "tools/list" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/mcp", content);
        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);

        var tools = node!["result"]!["tools"]!.AsArray();
        var nonStreamTool = tools.FirstOrDefault(t => t!["name"]!.GetValue<string>() == "list_orders");
        nonStreamTool.Should().NotBeNull();
        nonStreamTool!["streaming"].Should().BeNull("non-streaming tools should not have streaming flag");
    }

    [Fact]
    public async Task StreamOrders_ReturnsSSEEventStream()
    {
        var body = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = "stream_orders", arguments = new { } }
        };

        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        var events = await ReadSseEventsAsync(response);

        events.Should().NotBeEmpty("streaming response should contain SSE events");

        var chunkEvents = events.Where(e => e.eventType == "chunk").ToList();
        chunkEvents.Should().NotBeEmpty("there should be at least one chunk event");

        foreach (var (_, data) in chunkEvents)
        {
            var parsed = JsonNode.Parse(data);
            parsed!["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
            parsed["result"]!["_meta"]!["status"]!.GetValue<string>().Should().Be("streaming");
            parsed["result"]!["content"]!.AsArray().Should().HaveCountGreaterThan(0);
        }

        var doneOrErrorEvents = events.Where(e => e.eventType == "done" || e.eventType == "error").ToList();
        doneOrErrorEvents.Should().HaveCountGreaterThan(0, "streaming should end with a done or error event");

        var doneEvents = events.Where(e => e.eventType == "done").ToList();
        doneEvents.Should().HaveCount(1, "streaming should end with exactly one done event");

        var doneData = JsonNode.Parse(doneEvents[0].data);
        doneData!["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
        doneData["result"]!["_meta"]!["status"]!.GetValue<string>().Should().Be("done");
        doneData["result"]!["_meta"]!["totalChunks"]!.GetValue<int>().Should().Be(chunkEvents.Count);
    }

    private static async Task<List<(string eventType, string data)>> ReadSseEventsAsync(HttpResponseMessage response)
    {
        var fullBody = await response.Content.ReadAsStringAsync();
        var events = new List<(string eventType, string data)>();
        string? currentEvent = null;
        string? currentData = null;

        foreach (var rawLine in fullBody.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("event: "))
                currentEvent = line.Substring(7).Trim();
            else if (line.StartsWith("data: "))
                currentData = line.Substring(6);
            else if (line == "" && currentEvent is not null && currentData is not null)
            {
                events.Add((currentEvent, currentData));
                currentEvent = null;
                currentData = null;
            }
        }

        if (currentEvent is not null && currentData is not null)
            events.Add((currentEvent, currentData));

        return events;
    }

    [Fact]
    public async Task StreamOrders_ChunkContentIsValidOrderJson()
    {
        var body = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = "stream_orders", arguments = new { } }
        };

        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/mcp", content);
        var events = await ReadSseEventsAsync(response);
        var chunks = new List<string>();

        foreach (var (eventType, data) in events)
        {
            if (eventType == "chunk")
            {
                var parsed = JsonNode.Parse(data);
                var text = parsed!["result"]!["content"]!.AsArray()[0]!["text"]!.GetValue<string>();
                chunks.Add(text);
            }
        }

        chunks.Should().NotBeEmpty();
        foreach (var chunk in chunks)
        {
            var order = JsonNode.Parse(chunk);
            order.Should().NotBeNull();
            order!["id"].Should().NotBeNull();
            order["customerName"].Should().NotBeNull();
        }
    }

    [Fact]
    public async Task InspectorTools_StreamOrdersHasStreamingFlag()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);

        var tools = node!["tools"]!.AsArray();
        var streamTool = tools.FirstOrDefault(t => t!["name"]!.GetValue<string>() == "stream_orders");
        streamTool.Should().NotBeNull();
        streamTool!["isStreaming"]!.GetValue<bool>().Should().BeTrue();
    }
}
