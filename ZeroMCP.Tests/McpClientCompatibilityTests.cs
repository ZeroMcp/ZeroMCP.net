using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroMCP.Options;

namespace ZeroMCP.Tests;

// ---------------------------------------------------------------------------
// Client-compatibility tests
//
// These tests verify behaviour required by specific AI clients:
//
//   Codex    — GET /mcp with Accept: text/event-stream must open an SSE stream
//              notifications/initialized must return 202 Accepted (not 204)
//
//   Copilot  — resources/templates/list and prompts/list must return empty
//              lists (not -32601) even when the features are disabled, because
//              Copilot calls these methods unconditionally and treats Method Not
//              Found as "server unavailable"
//
//   Claude   — Same SSE-on-GET rule as Codex when using HTTP transport
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Factory: features disabled (EnableResources=false, EnablePrompts=false)
// Used to verify the "graceful empty list" behaviour required by Copilot.
// ---------------------------------------------------------------------------
public sealed class FeaturesDisabledFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<ZeroMCPOptions>(opts =>
            {
                opts.EnableResources = false;
                opts.EnablePrompts = false;
            });
        });
    }
}

// ===========================================================================
// Tests that run against the standard sample app (features enabled)
// ===========================================================================

public sealed class McpClientCompatibilityTests : IClassFixture<SampleAppWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpClientCompatibilityTests(SampleAppWebApplicationFactory factory)
        => _client = factory.CreateClient();

    // -----------------------------------------------------------------------
    // GET /mcp SSE — Codex / Claude HTTP transport
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetMcp_WithAcceptTextEventStream_Returns200()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Codex/Claude require GET /mcp with Accept: text/event-stream to return 200 OK");
    }

    [Fact]
    public async Task GetMcp_WithAcceptTextEventStream_ContentTypeIsTextEventStream()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream",
            "Codex requires Content-Type: text/event-stream so it can parse the SSE stream");
    }

    [Fact]
    public async Task GetMcp_WithAcceptTextEventStream_StreamIsOpen()
    {
        // The SSE stream must stay alive (not close immediately) so the client
        // can receive server-sent notifications/progress events.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The stream body must be readable and stay open — verified by checking
        // that we can successfully open it (the server has not closed the response body).
        var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        stream.CanRead.Should().BeTrue(
            "the SSE response body must be a readable, non-closed stream");
    }

    [Fact]
    public async Task GetMcp_WithAcceptTextEventStream_CacheControlIsNoCache()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        // Cache-Control: no-cache is required by the SSE spec so proxies don't buffer
        var cacheControl = response.Headers.CacheControl;
        cacheControl.Should().NotBeNull();
        cacheControl!.NoCache.Should().BeTrue(
            "SSE streams must carry Cache-Control: no-cache to prevent proxy buffering");
    }

    [Fact]
    public async Task GetMcp_WithoutAcceptHeader_ReturnsJsonDescription()
    {
        // Plain GET (browser / developer curl) must still return the human-readable
        // JSON description — SSE is only activated when the client explicitly asks for it.
        using var response = await _client.GetAsync("/mcp");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "a plain GET without Accept: text/event-stream should still return the JSON description");

        var body = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(body);
        node.Should().NotBeNull();
        node!["protocol"]!.GetValue<string>().Should().Be("MCP");
    }

    [Fact]
    public async Task GetMcp_WithAcceptApplicationJson_ReturnsJsonDescription()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    // -----------------------------------------------------------------------
    // Response ID type fidelity — Codex sends integer IDs and expects integer IDs back.
    // A JSON-RPC id of 0 (number) must echo back as 0 (number), not "0" (string).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResponseId_IntegerZero_EchoedAsIntegerNotString()
    {
        // Send a raw JSON body so the id is definitely a JSON number, not a string.
        var rawJson = """{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"codex","version":"1.0"}}}""";
        using var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var httpResponse = await _client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();

        // Parse the raw JSON so we can inspect the actual token type of "id"
        using var doc = JsonDocument.Parse(responseJson);
        var idElement = doc.RootElement.GetProperty("id");

        idElement.ValueKind.Should().Be(JsonValueKind.Number,
            "Codex sends id:0 as a JSON number; echoing it back as the string \"0\" causes " +
            "'response id: expected 0, got 0' type-mismatch errors during handshake");

        idElement.GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ResponseId_PositiveInteger_EchoedAsInteger()
    {
        var rawJson = """{"jsonrpc":"2.0","id":42,"method":"tools/list"}""";
        using var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var httpResponse = await _client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(responseJson);
        var idElement = doc.RootElement.GetProperty("id");

        idElement.ValueKind.Should().Be(JsonValueKind.Number,
            "integer request IDs must be echoed back as JSON numbers");
        idElement.GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task ResponseId_StringId_EchoedAsString()
    {
        // String IDs are also valid per JSON-RPC 2.0; these must NOT become numbers.
        var rawJson = """{"jsonrpc":"2.0","id":"req-1","method":"tools/list"}""";
        using var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var httpResponse = await _client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(responseJson);
        var idElement = doc.RootElement.GetProperty("id");

        idElement.ValueKind.Should().Be(JsonValueKind.String,
            "string request IDs must be echoed back as JSON strings");
        idElement.GetString().Should().Be("req-1");
    }

    // -----------------------------------------------------------------------
    // notifications/initialized — must return 202 Accepted (Codex)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NotificationsInitialized_Returns202Accepted()
    {
        var body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
            // no id — notifications are fire-and-forget
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/mcp", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "Codex requires notifications/initialized to return 202 Accepted, not 204 No Content");
    }

    [Fact]
    public async Task NotificationsInitialized_HasNoBody()
    {
        var body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/mcp", content);

        var responseBody = await response.Content.ReadAsStringAsync();
        responseBody.Should().BeNullOrEmpty(
            "notifications/initialized must return an empty body per the MCP streamable HTTP spec");
    }

    [Fact]
    public async Task NotificationsCancelled_Returns204()
    {
        // notifications/cancelled is a different notification; it should NOT be
        // changed to 202 — verify the targeted fix doesn't bleed over.
        var body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "notifications/cancelled",
            @params = new { requestId = "nonexistent-99" }
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/mcp", content);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "notifications/cancelled is not initialized and should still return 204");
    }

    // -----------------------------------------------------------------------
    // resources/list, resources/templates/list, prompts/list — enabled baseline
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResourcesTemplatesList_WhenEnabled_ReturnsResourceTemplatesKey()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 200,
            method = "resources/templates/list"
        });

        response.Should().HaveProperty("result");
        response["result"]!.AsObject().Should().HaveProperty("resourceTemplates",
            "resources/templates/list result must always carry the resourceTemplates key");
    }

    [Fact]
    public async Task PromptsList_WhenEnabled_ReturnsPromptsKey()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 201,
            method = "prompts/list"
        });

        response.Should().HaveProperty("result");
        response["result"]!.AsObject().Should().HaveProperty("prompts",
            "prompts/list result must always carry the prompts key");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<JsonObject> PostMcpAsync(object body)
    {
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpResponse = await _client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        return JsonNode.Parse(responseJson)!.AsObject();
    }
}

// ===========================================================================
// Tests that run against the features-disabled factory
// ===========================================================================

public sealed class McpClientCompatibilityDisabledFeaturesTests
    : IClassFixture<FeaturesDisabledFactory>
{
    private readonly HttpClient _client;

    public McpClientCompatibilityDisabledFeaturesTests(FeaturesDisabledFactory factory)
        => _client = factory.CreateClient();

    // -----------------------------------------------------------------------
    // Copilot calls resources/templates/list and prompts/list unconditionally.
    // A -32601 Method Not Found response causes Copilot to mark the server
    // unavailable.  Even when the features are disabled, these methods must
    // return empty lists.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResourcesTemplatesList_WhenResourcesDisabled_ReturnsEmptyList()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 300,
            method = "resources/templates/list"
        });

        response.Should().HaveProperty("result",
            "resources/templates/list must return a result (not an error) even when EnableResources=false");
        response.Should().NotHaveProperty("error");

        var templates = response["result"]!.AsObject()["resourceTemplates"]!.AsArray();
        templates.Should().BeEmpty(
            "an empty list is valid and preferable to a -32601 Method Not Found that breaks Copilot");
    }

    [Fact]
    public async Task ResourcesTemplatesList_WhenResourcesDisabled_DoesNotReturnMethodNotFound()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 301,
            method = "resources/templates/list"
        });

        if (response.ContainsKey("error"))
        {
            var code = response["error"]!.AsObject()["code"]!.GetValue<int>();
            code.Should().NotBe(-32601,
                "returning -32601 Method Not Found for resources/templates/list causes Copilot to mark the server unavailable");
        }
    }

    [Fact]
    public async Task ResourcesList_WhenResourcesDisabled_ReturnsEmptyList()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 302,
            method = "resources/list"
        });

        response.Should().HaveProperty("result");
        response.Should().NotHaveProperty("error");

        var resources = response["result"]!.AsObject()["resources"]!.AsArray();
        resources.Should().BeEmpty();
    }

    [Fact]
    public async Task PromptsList_WhenPromptsDisabled_ReturnsEmptyList()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 303,
            method = "prompts/list"
        });

        response.Should().HaveProperty("result",
            "prompts/list must return a result (not an error) even when EnablePrompts=false");
        response.Should().NotHaveProperty("error");

        var prompts = response["result"]!.AsObject()["prompts"]!.AsArray();
        prompts.Should().BeEmpty(
            "an empty list is valid and preferable to a -32601 that breaks Copilot");
    }

    [Fact]
    public async Task PromptsList_WhenPromptsDisabled_DoesNotReturnMethodNotFound()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 304,
            method = "prompts/list"
        });

        if (response.ContainsKey("error"))
        {
            var code = response["error"]!.AsObject()["code"]!.GetValue<int>();
            code.Should().NotBe(-32601,
                "returning -32601 for prompts/list causes Copilot to mark the server unavailable");
        }
    }

    [Fact]
    public async Task Initialize_WhenFeaturesDisabled_DoesNotAdvertiseResourcesOrPrompts()
    {
        // When features are disabled, capabilities must NOT advertise resources/prompts
        // so well-behaved clients (Claude) don't call methods that aren't present.
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 305,
            method = "initialize",
            @params = new
            {
                protocolVersion = McpProtocolConstants.ProtocolVersion,
                clientInfo = new { name = "test", version = "1.0" }
            }
        });

        var capabilities = response["result"]!.AsObject()["capabilities"]!.AsObject();
        capabilities.Should().NotHaveProperty("resources",
            "when EnableResources=false, resources must not appear in capabilities");
        capabilities.Should().NotHaveProperty("prompts",
            "when EnablePrompts=false, prompts must not appear in capabilities");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<JsonObject> PostMcpAsync(object body)
    {
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpResponse = await _client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        return JsonNode.Parse(responseJson)!.AsObject();
    }
}
