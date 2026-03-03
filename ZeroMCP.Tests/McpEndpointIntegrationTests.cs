using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZeroMCP;
using ZeroMCP.Options;
using Xunit;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;

namespace ZeroMCP.Tests;

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
        toolNames.Should().Contain("health_check");
        toolNames.Should().Contain("list_customers");
        toolNames.Should().Contain("get_customer");
        toolNames.Should().Contain("get_customer_orders");
        toolNames.Should().Contain("create_customer");
        toolNames.Should().Contain("list_products");
        toolNames.Should().Contain("get_product");
        toolNames.Should().Contain("create_product");
        toolNames.Should().NotContain("delete_order");
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
        toolNames.Should().Contain("admin_health", "admin-key adds Admin role so admin_health is visible");
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
        }, new Dictionary<string, string> { { "Bearer: ", "INVALID" } } );
         
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
        healthCheck.Should().NotBeNull("health_check tool should be present");
        healthCheck!.Should().HaveProperty("tags");
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
        names.Should().Contain("health_check");
        names.Should().Contain("list_orders");
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
        healthCheck.Should().NotBeNull("health_check should be in inspector");
        healthCheck!.Should().HaveProperty("tags");
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
        adminHealth.Should().NotBeNull("admin_health should be in inspector (shows all registered tools)");
        adminHealth!.Should().HaveProperty("requiredRoles");
        var roles = adminHealth["requiredRoles"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        roles.Should().Contain("Admin");
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
