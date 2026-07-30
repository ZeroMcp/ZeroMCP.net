using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace ZeroMCP.Tests;

/// <summary>
/// Compatibility tests for MCP protocol version 2026-07-28 (modern / dual-era).
/// </summary>
public sealed class McpModernProtocolTests : IClassFixture<SampleAppWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpModernProtocolTests(SampleAppWebApplicationFactory factory)
        => _client = factory.CreateClient();

    private static object ModernMeta(string version = McpProtocolConstants.ModernProtocolVersion) => new Dictionary<string, object>
    {
        [McpProtocolConstants.MetaProtocolVersion] = version,
        [McpProtocolConstants.MetaClientInfo] = new { name = "modern-test", version = "1.0" },
        [McpProtocolConstants.MetaClientCapabilities] = new { }
    };

    private async Task<(HttpResponseMessage Http, JsonObject Body)> PostModernAsync(
        string method,
        object? extraParams = null,
        string? mcpName = null,
        int id = 1,
        string version = McpProtocolConstants.ModernProtocolVersion)
    {
        var parameters = new Dictionary<string, object?> { ["_meta"] = ModernMeta(version) };
        if (extraParams is not null)
        {
            var json = JsonSerializer.Serialize(extraParams);
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
                parameters[prop.Name] = prop.Value.Clone();
        }

        var payload = new { jsonrpc = "2.0", id, method, @params = parameters };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = content };
        request.Headers.TryAddWithoutValidation(McpProtocolConstants.HeaderProtocolVersion, version);
        request.Headers.TryAddWithoutValidation(McpProtocolConstants.HeaderMethod, method);
        if (mcpName is not null)
            request.Headers.TryAddWithoutValidation(McpProtocolConstants.HeaderName, mcpName);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var http = await _client.SendAsync(request);
        var text = await http.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(text)
            ? new JsonObject()
            : JsonNode.Parse(text)!.AsObject();
        return (http, body);
    }

    [Fact]
    public async Task ServerDiscover_ReturnsSupportedVersionsAndCapabilities()
    {
        var (http, body) = await PostModernAsync("server/discover");
        http.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().HaveProperty("result");
        var result = body["result"]!.AsObject();
        result["supportedVersions"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().Contain(McpProtocolConstants.ModernProtocolVersion)
            .And.Contain(McpProtocolConstants.LegacyProtocolVersion);
        result["serverInfo"]!.AsObject()["name"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        result["capabilities"]!.AsObject().Should().HaveProperty("tools");
        result["resultType"]!.GetValue<string>().Should().Be("complete");
        result["ttlMs"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(0);
        result["cacheScope"]!.GetValue<string>().Should().BeOneOf("public", "private");
        result["_meta"]!.AsObject().Should().HaveProperty(McpProtocolConstants.MetaServerInfo);
    }

    [Fact]
    public async Task ToolsList_Modern_IncludesCacheHintsAndResultType()
    {
        var (http, body) = await PostModernAsync("tools/list");
        http.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = body["result"]!.AsObject();
        result.Should().HaveProperty("tools");
        result["resultType"]!.GetValue<string>().Should().Be("complete");
        result.Should().HaveProperty("ttlMs");
        result.Should().HaveProperty("cacheScope");
        result["_meta"]!.AsObject().Should().HaveProperty(McpProtocolConstants.MetaServerInfo);
    }

    [Fact]
    public async Task ToolsCall_Modern_RequiresMcpNameHeader()
    {
        // Missing Mcp-Name header → HeaderMismatch (-32020) with HTTP 400
        var parameters = new Dictionary<string, object?>
        {
            ["_meta"] = ModernMeta(),
            ["name"] = "health_check",
            ["arguments"] = new { }
        };
        var payload = new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = parameters };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = content };
        request.Headers.TryAddWithoutValidation(McpProtocolConstants.HeaderProtocolVersion, McpProtocolConstants.ModernProtocolVersion);
        request.Headers.TryAddWithoutValidation(McpProtocolConstants.HeaderMethod, "tools/call");
        // deliberately omit Mcp-Name

        var http = await _client.SendAsync(request);
        var text = await http.Content.ReadAsStringAsync();
        http.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonNode.Parse(text)!.AsObject();
        body["error"]!["code"]!.GetValue<int>().Should().Be(McpProtocolConstants.ErrorHeaderMismatch);
    }

    [Fact]
    public async Task ToolsCall_Modern_WithMatchingHeaders_Succeeds()
    {
        var (http, body) = await PostModernAsync(
            "tools/call",
            extraParams: new { name = "health_check", arguments = new { } },
            mcpName: "health_check");

        http.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().HaveProperty("result");
        var result = body["result"]!.AsObject();
        result["resultType"]!.GetValue<string>().Should().Be("complete");
        result.Should().HaveProperty("content");
        result["_meta"]!.AsObject().Should().HaveProperty(McpProtocolConstants.MetaServerInfo);
    }

    [Fact]
    public async Task UnsupportedProtocolVersion_ReturnsMinus32022()
    {
        var (http, body) = await PostModernAsync("tools/list", version: "1900-01-01");
        http.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body["error"]!["code"]!.GetValue<int>().Should().Be(McpProtocolConstants.ErrorUnsupportedProtocolVersion);
        var data = body["error"]!["data"]!.AsObject();
        data["requested"]!.GetValue<string>().Should().Be("1900-01-01");
        data["supported"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task HeaderMethodMismatch_ReturnsMinus32020()
    {
        var parameters = new Dictionary<string, object?> { ["_meta"] = ModernMeta() };
        var payload = new { jsonrpc = "2.0", id = 3, method = "tools/list", @params = parameters };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = content };
        request.Headers.TryAddWithoutValidation(McpProtocolConstants.HeaderProtocolVersion, McpProtocolConstants.ModernProtocolVersion);
        request.Headers.TryAddWithoutValidation(McpProtocolConstants.HeaderMethod, "tools/call"); // mismatch

        var http = await _client.SendAsync(request);
        http.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonNode.Parse(await http.Content.ReadAsStringAsync())!.AsObject();
        body["error"]!["code"]!.GetValue<int>().Should().Be(McpProtocolConstants.ErrorHeaderMismatch);
        body["error"]!["message"]!.GetValue<string>().Should().Contain("Mcp-Method");
    }

    [Fact]
    public async Task SubscriptionsListen_OpensSseAndAcknowledges()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var parameters = new Dictionary<string, object?>
        {
            ["_meta"] = ModernMeta(),
            ["notifications"] = new { toolsListChanged = true }
        };
        var payload = new { jsonrpc = "2.0", id = 42, method = "subscriptions/listen", @params = parameters };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = content };
        request.Headers.TryAddWithoutValidation(McpProtocolConstants.HeaderProtocolVersion, McpProtocolConstants.ModernProtocolVersion);
        request.Headers.TryAddWithoutValidation(McpProtocolConstants.HeaderMethod, "subscriptions/listen");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var http = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        http.StatusCode.Should().Be(HttpStatusCode.OK);
        http.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        await using var stream = await http.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var line1 = await reader.ReadLineAsync(cts.Token);
        var line2 = await reader.ReadLineAsync(cts.Token);
        // event: message / data: {...}
        var combined = $"{line1}\n{line2}";
        combined.Should().Contain("notifications/subscriptions/acknowledged");
        combined.Should().Contain("toolsListChanged");
    }

    [Fact]
    public async Task LegacyInitialize_StillWorksAlongsideModern()
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 9,
            method = "initialize",
            @params = new
            {
                protocolVersion = McpProtocolConstants.LegacyProtocolVersion,
                clientInfo = new { name = "legacy", version = "1.0" }
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var http = await _client.PostAsync("/mcp", content);
        http.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonNode.Parse(await http.Content.ReadAsStringAsync())!.AsObject();
        body["result"]!["protocolVersion"]!.GetValue<string>().Should().Be(McpProtocolConstants.LegacyProtocolVersion);
    }
}
